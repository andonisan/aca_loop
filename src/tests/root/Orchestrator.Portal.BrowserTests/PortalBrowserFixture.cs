using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Orchestrator.Constants;
using Orchestrator.Modules.Backlog.Contracts;
using Orchestrator.Portal.ReadModel;
using Orchestrator.Portal.Registry;

namespace Orchestrator.Portal.BrowserTests;

/// <summary>
/// The portal, served over a real port, with every panel's state under the test's control.
/// <para>
/// A real Kestrel listener rather than <c>WebApplicationFactory</c>'s in-memory server, because a browser
/// needs a URL it can actually navigate to. The factory is still used for everything else it is good at —
/// composing the app, overriding services, the stand-in auth scheme — so the two are started side by side.
/// </para>
/// <para>
/// One host and one browser for the whole suite, with stub state reset per test (design D5). Reset is
/// assigning fresh stub state rather than Respawn, since nothing here has a database.
/// </para>
/// </summary>
public sealed class PortalBrowserFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    const string SchemeName = "BrowserTests";

    IHost? _kestrel;
    IPlaywright? _playwright;

    internal StubPanelReader Panels { get; } = new();

    /// <summary>
    /// Two stacks, overridden rather than taken from <c>stacks.yaml</c>.
    /// <para>
    /// The distinctions this suite exists for are comparative — "unreadable is not zero" is only observable
    /// with one of each side by side — and the shipped registry has a single entry. Adding a fictional
    /// client to production configuration to satisfy a test would be the wrong direction, and the registry
    /// is an authorization artifact, so it is the last file to start putting test fixtures in.
    /// </para>
    /// </summary>
    internal static StackRegistry TestRegistry { get; } =
        new([TestStack("alpha", "Alpha Client"), TestStack("beta", "Beta Client")]);

    public const string HealthyStack = "alpha";
    public const string BrokenStack = "beta";

    public IBrowser Browser { get; private set; } = null!;

    public string BaseUrl { get; private set; } = "";

    /// <summary>Roles the signed-in principal carries. Tests that assert on what an Operator can do versus
    /// a Reader change this before navigating.</summary>
    public string[] Roles { get; set; } = [PortalRoles.Operator];

    public async Task InitializeAsync()
    {
        // Touching Services forces the host to build, which starts Kestrel via CreateHost below.
        _ = Services;

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync();
    }

    public new async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        _playwright?.Dispose();

        if (_kestrel is not null)
        {
            await _kestrel.StopAsync();
            _kestrel.Dispose();
        }

        await base.DisposeAsync();
    }

    /// <summary>A page with the suite's stub state, at the portal's real address.</summary>
    public async Task<IPage> NewPageAsync()
    {
        var context = await Browser.NewContextAsync(
            new BrowserNewContextOptions { BaseURL = BaseUrl }
        );
        return await context.NewPageAsync();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Order matters and is not obvious. The TestServer host must be built *before* the builder is
        // switched to Kestrel, or it inherits Kestrel too and the factory's internals fail casting its
        // server to TestServer. Building it first captures the un-modified configuration.
        var testHost = builder.Build();

        // Now a second host on a real ephemeral port, which is what the browser navigates to. Port 0
        // rather than a fixed one: a hard-coded port turns a stale process into a confusing failure in
        // some unrelated run.
        builder.ConfigureWebHost(web => web.UseKestrel().UseUrls("http://127.0.0.1:0"));
        _kestrel = builder.Build();

        // Started before the test host: with minimal hosting's deferred builder, the listening address is
        // not available until the server has actually started.
        _kestrel.Start();

        BaseUrl =
            _kestrel
                .Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()
                ?.Addresses.LastOrDefault()
            ?? throw new InvalidOperationException("Kestrel reported no listening address");

        testHost.Start();
        return testHost;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Entra must not become a network dependency of a test suite -- the same reason the functional
        // tests use a stand-in scheme. What is under test here is the page, not the identity provider.
        builder.UseSetting("AzureAd:TenantId", "11111111-1111-1111-1111-111111111111");
        builder.UseSetting("AzureAd:ClientId", "22222222-2222-2222-2222-222222222222");
        builder.UseSetting("AzureAd:Instance", "https://login.microsoftonline.com/");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IPanelReader>(Panels);
            services.AddSingleton(TestRegistry);

            services
                .AddAuthentication(SchemeName)
                .AddScheme<AuthenticationSchemeOptions, StubAuthHandler>(SchemeName, _ => { });
            services.AddSingleton(this);
        });
    }

    /// <summary>Coordinates are shaped like real ones but point nowhere: nothing in this suite reaches
    /// Azure or a vendor, because every read is intercepted at <see cref="IPanelReader"/>.</summary>
    static RegisteredStack TestStack(string id, string displayName) =>
        new(
            id,
            displayName,
            MappingFilePath: $"{id}.mapping.yaml",
            new StackAzureCoordinates(
                SubscriptionId: "00000000-0000-0000-0000-000000000000",
                ResourceGroup: $"rg-{id}",
                QueueServiceUri: new Uri($"https://st{id}.queue.core.windows.net"),
                QueueName: "ai-jobs",
                JobName: $"ca-{id}-agent",
                LogAnalyticsWorkspaceId: "00000000-0000-0000-0000-000000000000"
            ),
            new StackVendorCoordinates(
                Vendor.GitHub,
                Project: null,
                Repo: $"example/{id}",
                ReadCredentialSecretName: $"Portal--{id}--GitHubReadToken",
                BaseUrl: new Uri("https://api.github.com"),
                Deployment: null
            )
        );

    sealed class StubAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        PortalBrowserFixture fixture
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.Name, "browser@example.com") };
            claims.AddRange(fixture.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName))
            );
        }
    }
}
