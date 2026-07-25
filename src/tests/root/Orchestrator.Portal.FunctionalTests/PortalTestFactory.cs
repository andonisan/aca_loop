using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Orchestrator.Portal.FunctionalTests;

/// <summary>
/// Hosts the portal with a stand-in authentication scheme. Entra itself is not under test here (and
/// must not become a network dependency of the test suite) — what is under test is everything the
/// portal does once a principal exists or does not: which endpoints are reachable unauthenticated,
/// and what a signed-in user sees with and without a portal role.
/// </summary>
sealed class PortalTestFactory : WebApplicationFactory<Program>
{
    public const string SchemeName = "PortalTests";

    /// <summary>Roles the next request should carry. Null means unauthenticated.</summary>
    public string[]? Roles { get; set; }

    public string UserName { get; set; } = "tester@example.com";

    /// <summary>Extra configuration applied before the host starts (environment name, feature flags).</summary>
    public Dictionary<string, string?> Settings { get; } = [];

    /// <summary>
    /// Set false to leave the app's own authentication in place. Only the tests that assert on scheme
    /// *selection* need this — everything else wants the stand-in principal.
    /// </summary>
    public bool OverrideAuthentication { get; set; } = true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("AzureAd:TenantId", "11111111-1111-1111-1111-111111111111");
        builder.UseSetting("AzureAd:ClientId", "22222222-2222-2222-2222-222222222222");

        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Registry:FilePath"] = "stacks.yaml" }
            )
        );

        builder.ConfigureTestServices(services =>
        {
            if (!OverrideAuthentication)
            {
                return;
            }

            services.AddSingleton(this);

            // Replaces OpenID Connect as the default scheme for both authenticate and challenge, so
            // an unauthenticated request produces a plain 401 instead of reaching out to Entra.
            services
                .AddAuthentication(SchemeName)
                .AddScheme<AuthenticationSchemeOptions, StubAuthenticationHandler>(
                    SchemeName,
                    _ => { }
                );
        });
    }

    sealed class StubAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        PortalTestFactory factory
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (factory.Roles is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            Claim[] claims =
            [
                new(ClaimTypes.Name, factory.UserName),
                .. factory.Roles.Select(role => new Claim(ClaimTypes.Role, role)),
            ];

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));

            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName))
            );
        }
    }
}
