using Shouldly;

namespace Orchestrator.Portal.BrowserTests;

/// <summary>
/// Task 1.4: prove the harness before anything depends on it.
/// <para>
/// Deliberately trivial. A harness whose first failure is ambiguous between "the test is wrong" and "the
/// harness is wrong" wastes exactly the time it was built to save, so there is one test that fails only if
/// the plumbing is broken — browser launched, app served on a real port, authenticated request rendered.
/// </para>
/// </summary>
[Collection(BrowserCollection.Name)]
public class Harness_Should_Constraint(PortalBrowserFixture fixture)
{
    [Fact]
    public async Task Portal_Should_RenderTheStacksPageInABrowser()
    {
        var page = await fixture.NewPageAsync();

        var response = await page.GotoAsync("/");

        response!.Ok.ShouldBeTrue();
        // The h1 the stacks page renders. Text is acceptable here and only here: this asserts the harness
        // reached the app at all, so keying it to a testid would be testing the testid.
        (await page.Locator("h1").InnerTextAsync()).ShouldBe("Stacks");
    }
}

[CollectionDefinition(Name)]
public sealed class BrowserCollection : ICollectionFixture<PortalBrowserFixture>
{
    public const string Name = "portal-browser";
}
