using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Orchestrator.Constants;
using Orchestrator.Portal.LocalDev;
using Shouldly;

namespace Orchestrator.Portal.FunctionalTests;

/// <summary>
/// The local-development stand-in principal is authentication-bypass code, so the fence around it is
/// what is worth testing: the opt-in flag must do nothing outside the Development environment. The
/// deployed image runs as Production, which is what makes that check load-bearing.
///
/// <para>
/// The negative cases assert on which scheme the host *selected* rather than on an HTTP status. Once
/// real OpenID Connect is in play, any request would try to fetch a discovery document from Entra —
/// making the test both slow and network-dependent, and turning a refused page into a thrown
/// exception rather than a status code. Scheme selection is the actual invariant anyway.
/// </para>
/// </summary>
public sealed class LocalDevAuth_Should_Constraint
{
    static PortalTestFactory Configured(string environment, bool devAuthEnabled) =>
        new()
        {
            // The app's own authentication stays in place; scheme selection is under test.
            OverrideAuthentication = false,
            Settings =
            {
                ["environment"] = environment,
                ["Portal:LocalDevAuth:Enabled"] = devAuthEnabled ? "true" : "false",
                ["Portal:LocalDevAuth:Roles:0"] = PortalRoles.Reader,
            },
        };

    static async Task<string?> DefaultSchemeAsync(PortalTestFactory factory)
    {
        // Touch the server so the host is built, then read back what it wired.
        using var client = factory.CreateClient();
        await client.GetAsync(HealthChecks.HealthEndpoint);

        var provider = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = await provider.GetDefaultAuthenticateSchemeAsync();

        return scheme?.Name;
    }

    [Fact]
    public async Task DevelopmentWithTheFlagOn_Should_SelectTheStandInScheme()
    {
        using var factory = Configured("Development", devAuthEnabled: true);

        (await DefaultSchemeAsync(factory)).ShouldBe(LocalDevAuthentication.SchemeName);
    }

    [Fact]
    public async Task ProductionWithTheFlagOn_Should_StillSelectOpenIdConnect()
    {
        using var factory = Configured("Production", devAuthEnabled: true);

        // The flag is deliberately inert outside Development — this is the fence.
        (await DefaultSchemeAsync(factory)).ShouldBe(OpenIdConnectDefaults.AuthenticationScheme);
    }

    [Fact]
    public async Task DevelopmentWithTheFlagOff_Should_SelectOpenIdConnect()
    {
        using var factory = Configured("Development", devAuthEnabled: false);

        (await DefaultSchemeAsync(factory)).ShouldBe(OpenIdConnectDefaults.AuthenticationScheme);
    }

    [Fact]
    public async Task ProductionWithTheFlagOn_Should_NotRegisterTheStandInSchemeAtAll()
    {
        using var factory = Configured("Production", devAuthEnabled: true);
        using var client = factory.CreateClient();
        await client.GetAsync(HealthChecks.HealthEndpoint);

        var provider = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        // Not merely "not the default" — absent, so it cannot be reached by naming it either.
        (await provider.GetSchemeAsync(LocalDevAuthentication.SchemeName)).ShouldBeNull();
    }

    [Fact]
    public async Task TheStandInPrincipal_Should_RenderPagesWithoutAnAppRegistration()
    {
        using var factory = Configured("Development", devAuthEnabled: true);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        // The whole point of the affordance: the UI is workable locally with no Entra tenant.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("Stacks");
    }

    [Fact]
    public async Task TheStandInPrincipal_Should_LeaveProbesAnonymous()
    {
        using var factory = Configured("Development", devAuthEnabled: true);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(HealthChecks.HealthEndpoint);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
