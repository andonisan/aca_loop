using System.Net;
using Orchestrator.Constants;
using Shouldly;

namespace Orchestrator.Portal.FunctionalTests;

/// <summary>
/// Locks the portal's access boundary end to end through the real middleware pipeline. The invariant
/// worth a test is "protected by default": a page added later must not become anonymously reachable
/// because someone forgot an attribute (portal-auth spec).
/// </summary>
public sealed class PortalAccess_Should_Constraint : IDisposable
{
    readonly PortalTestFactory factory = new();

    public void Dispose() => factory.Dispose();

    HttpClient Anonymous()
    {
        factory.Roles = null;
        return factory.CreateClient(new() { AllowAutoRedirect = false });
    }

    HttpClient SignedInWith(params string[] roles)
    {
        factory.Roles = roles;
        return factory.CreateClient(new() { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData(HealthChecks.HealthEndpoint)]
    [InlineData(HealthChecks.AliveEndpoint)]
    public async Task Probes_Should_BeReachableAnonymously(string endpoint)
    {
        // Platform probes must not depend on Entra being reachable.
        var response = await Anonymous().GetAsync(endpoint);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Probes_Should_ExposeNoStackData()
    {
        var body = await Anonymous().GetStringAsync(HealthChecks.HealthEndpoint);

        body.ShouldNotContain("dsconnect");
        body.ShouldNotContain("rg-");
    }

    [Fact]
    public async Task AnyPage_Should_RejectAnAnonymousRequest()
    {
        var response = await Anonymous().GetAsync("/");

        // Unauthorized here rather than a redirect only because the test scheme stands in for OIDC;
        // against Entra this same failure produces the 302 to the authorize endpoint. What is being
        // pinned is that the request is not served.
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnUnknownPath_Should_StillRequireAuthentication()
    {
        // A 404 for an anonymous caller would leak which paths exist; the fallback policy runs first.
        var response = await Anonymous().GetAsync("/no-such-page");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AReader_Should_SeeTheStacksView()
    {
        var body = await SignedInWith(PortalRoles.Reader).GetStringAsync("/");

        body.ShouldContain("Stacks");
        body.ShouldContain("dsconnect-dev");
    }

    [Fact]
    public async Task AnOperator_Should_SeeTheStacksView()
    {
        var body = await SignedInWith(PortalRoles.Operator).GetStringAsync("/");

        body.ShouldContain("Stacks");
    }

    [Fact]
    public async Task AUserWithNoPortalRole_Should_BeToldWhichRolesToAskFor()
    {
        var body = await SignedInWith("SomeUnrelated.Role").GetStringAsync("/");

        // Signed in, so the answer is an explanation naming the roles -- not a bare 403 and not any
        // stack data.
        body.ShouldContain("No portal access");
        body.ShouldContain(PortalRoles.Reader);
        body.ShouldContain(PortalRoles.Operator);
        body.ShouldNotContain("dsconnect-dev");

        // The display name is asserted separately from the id on purpose. The id leaks through a stack
        // link's href, so the assertion above happens to cover markup that renders links -- but the
        // sidebar renders the *display name* as its label, and "ds-connect (dev)" does not contain the
        // string "dsconnect-dev". Without this line, chrome that shows client names outside the Read
        // gate would pass the test while leaking exactly the data the portal-auth spec forbids.
        body.ShouldNotContain("ds-connect (dev)");
    }
}
