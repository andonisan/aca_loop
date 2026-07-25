using Microsoft.Playwright;
using Orchestrator.Constants;
using Orchestrator.Modules.Backlog.Contracts;
using Orchestrator.Portal.Presentation;
using Orchestrator.Portal.ReadModel;
using Shouldly;
using static Microsoft.Playwright.Assertions;

namespace Orchestrator.Portal.BrowserTests;

/// <summary>
/// Task 3.4. That the server refuses a verb to a reader is covered by the functional tests; this covers
/// something different and not implied by it — that the reader is never shown the button in the first
/// place. An affordance offered and then refused is a worse experience than one never offered, and on a
/// page whose verbs spend client money it also invites the click.
/// </summary>
public class VerbAffordances_Should_Constraint(PortalBrowserFixture fixture) : BrowserTest(fixture)
{
    const string Stack = PortalBrowserFixture.HealthyStack;

    [Fact]
    public async Task Operator_Should_SeeBothVerbs()
    {
        Fixture.Roles = [PortalRoles.Operator];

        var page = await ArrangeAndGoAsync();

        // The control case. Without it the reader assertions below would pass for the uninteresting reason
        // that nobody sees these buttons -- which is exactly what a selector typo would produce.
        await Expect(page.GetByTestId("dispatch-item")).ToHaveCountAsync(1);
        await Expect(page.GetByTestId("cancel-execution")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task Reader_Should_SeeNeitherVerb()
    {
        Fixture.Roles = [PortalRoles.Reader];

        var page = await ArrangeAndGoAsync();

        await Expect(page.GetByTestId("dispatch-item")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("cancel-execution")).ToHaveCountAsync(0);

        // Still a full reader, not a locked-out one: the panels the verbs sit in are all there.
        await Expect(page.GetByTestId("panel-gated")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("panel-pods")).ToBeVisibleAsync();
    }

    /// <summary>One gated item and one running execution — the two states that offer a verb at all.</summary>
    async Task<IPage> ArrangeAndGoAsync()
    {
        Fixture.Panels.Available(QueueDepth, Stack, 1);
        Fixture.Panels.Available<IReadOnlyList<PodExecution>>(
            PodExecutions,
            Stack,
            [
                new PodExecution(
                    "exec-running",
                    PodExecutionState.Running,
                    "Running",
                    new DateTimeOffset(2026, 7, 25, 11, 0, 0, TimeSpan.Zero),
                    TimeSpan.FromMinutes(2)
                ),
            ]
        );
        Fixture.Panels.Available<IReadOnlyList<BacklogItem>>(
            GatedItems,
            Stack,
            [
                new BacklogItem(
                    "42",
                    "Add a thing",
                    "status:ready-for-implementation",
                    null,
                    new DateTimeOffset(2026, 7, 25, 10, 0, 0, TimeSpan.Zero),
                    null
                ),
            ]
        );
        Fixture.Panels.Available(History7d, Stack, new RunHistory(TimeSpan.FromDays(7), [], 0, 0));

        var page = await Fixture.NewPageAsync();
        var response = await page.GotoAsync($"/stacks/{Stack}");

        response!.Ok.ShouldBeTrue();
        return page;
    }
}
