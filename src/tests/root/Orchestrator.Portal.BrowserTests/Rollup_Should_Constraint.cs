using Microsoft.Playwright;
using Orchestrator.Portal.ReadModel;
using Shouldly;
using static Microsoft.Playwright.Assertions;

namespace Orchestrator.Portal.BrowserTests;

/// <summary>
/// The cross-stack rollup's two claims that live entirely in markup, and so are invisible to every other
/// kind of test: a cell nobody could read must not look like a cell that read zero, and a stack whose
/// every source is down must still occupy a row.
/// </summary>
public class Rollup_Should_Constraint(PortalBrowserFixture fixture) : BrowserTest(fixture)
{
    /// <summary>
    /// Task 2.2, the requirement this whole suite exists for. "Nothing is queued" and "we could not ask"
    /// are different facts and only the first is safe to act on, but a rollup renders both as a small
    /// cell in the same column. The difference is entirely presentational, which is exactly why no unit
    /// or HTTP-level test can hold it.
    /// </summary>
    [Fact]
    public async Task Rollup_Should_DistinguishUnreadableFromZero()
    {
        Fixture.Panels.Available(QueueDepth, PortalBrowserFixture.HealthyStack, 0);
        Fixture.Panels.Failing<int>(
            QueueDepth,
            PortalBrowserFixture.BrokenStack,
            "queue unreachable"
        );

        var page = await GoToOverviewAsync();

        var readable = QueueCell(page, PortalBrowserFixture.HealthyStack);
        var unreadable = QueueCell(page, PortalBrowserFixture.BrokenStack);

        await Expect(readable.GetByTestId("unreadable")).ToHaveCountAsync(0);
        await Expect(unreadable.GetByTestId("unreadable")).ToBeVisibleAsync();

        // The unreadable cell must carry no number at all. Rendering the last known depth, or a zero
        // placeholder, would be worse than rendering nothing: an operator reading a digit has no way to
        // know it was invented.
        (await unreadable.InnerTextAsync()).ShouldNotMatch(@"\d");

        // And the readable one must actually say zero -- otherwise the assertion above passes for the
        // uninteresting reason that neither cell shows anything.
        (await readable.InnerTextAsync()).ShouldContain("0");
    }

    /// <summary>
    /// Task 2.3. Dropping the row would be the worst available failure: the operator sees a shorter list
    /// with no gap in it, and a stack in trouble is precisely the one that disappears.
    /// </summary>
    [Fact]
    public async Task Rollup_Should_KeepARowForAStackWhoseEverySourceFails()
    {
        Fixture.Panels.Available(QueueDepth, PortalBrowserFixture.HealthyStack, 3);
        Fixture.Panels.Available<IReadOnlyList<PodExecution>>(
            PodExecutions,
            PortalBrowserFixture.HealthyStack,
            []
        );
        Fixture.Panels.Available(
            History7d,
            PortalBrowserFixture.HealthyStack,
            new RunHistory(TimeSpan.FromDays(7), [], 0, 0)
        );

        // Nothing arranged for the broken stack at all, which the stub renders as every source failing --
        // the same shape as a stack whose subscription is unreachable.

        var page = await GoToOverviewAsync();

        await Expect(page.GetByTestId("rollup-row")).ToHaveCountAsync(2);

        var row = page.Locator($"[data-stack-id='{PortalBrowserFixture.BrokenStack}']");
        await Expect(row).ToBeVisibleAsync();

        // Every one of its four data cells reports unreadable, rather than the row thinning out to a name.
        await Expect(row.GetByTestId("unreadable")).ToHaveCountAsync(4);
    }

    async Task<IPage> GoToOverviewAsync()
    {
        var page = await Fixture.NewPageAsync();
        var response = await page.GotoAsync("/overview");

        response!.Ok.ShouldBeTrue();
        return page;
    }

    static ILocator QueueCell(IPage page, string stackId) =>
        page.Locator($"[data-stack-id='{stackId}']").GetByTestId("cell-queue");
}
