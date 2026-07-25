using Microsoft.Playwright;
using Orchestrator.Modules.Backlog.Contracts;
using Orchestrator.Portal.Operations;
using Orchestrator.Portal.Presentation;
using Orchestrator.Portal.ReadModel;
using Shouldly;
using static Microsoft.Playwright.Assertions;

namespace Orchestrator.Portal.BrowserTests;

/// <summary>
/// Tasks 3.2 and 3.3: how a verb's outcome reaches the operator.
/// <para>
/// The verbs are stubbed, so what is asserted here is the rendering, not the deciding — that a refusal
/// looks like a refusal and carries the live state, not that the verb was right to refuse. The deciding is
/// the implementation's claim and belongs to a unit test; the rendering is the claim no other kind of test
/// can hold. See <see cref="StubVerbs"/>.
/// </para>
/// </summary>
public class VerbOutcomes_Should_Constraint(PortalBrowserFixture fixture) : BrowserTest(fixture)
{
    const string Stack = PortalBrowserFixture.HealthyStack;

    /// <summary>
    /// Task 3.2. Three outcomes, three distinct renderings. The pair that matters is refused against
    /// failed: one means the board said no and the operator can act on it, the other means the portal
    /// broke and nothing they do on the board will help.
    /// </summary>
    [Theory]
    [InlineData(VerbOutcome.Performed, "performed")]
    [InlineData(VerbOutcome.Refused, "refused")]
    [InlineData(VerbOutcome.Failed, "failed")]
    public async Task Dispatch_Should_ReportItsOutcome(VerbOutcome outcome, string rendered)
    {
        Fixture.Verbs.Dispatch = new DispatchResult(outcome, "status:in-progress", "some detail");

        var page = await ArrangeAndGoAsync();
        await page.GetByTestId("dispatch-item").ClickAsync();

        var result = page.GetByTestId("verb-result");
        await Expect(result).ToBeVisibleAsync();
        await Expect(result).ToHaveAttributeAsync("data-outcome", rendered);
    }

    /// <summary>
    /// Task 3.2's second half. A refusal that does not say what state the item is actually in tells the
    /// operator only that nothing happened — true, and useless. The live state is what turns it into
    /// something they can go and fix.
    /// </summary>
    [Fact]
    public async Task Refusal_Should_ShowTheLiveStateThatCausedIt()
    {
        Fixture.Verbs.Dispatch = new DispatchResult(
            VerbOutcome.Refused,
            "status:in-review",
            "'status:in-review' is not a gate state for this stack."
        );

        var page = await ArrangeAndGoAsync();
        await page.GetByTestId("dispatch-item").ClickAsync();

        await Expect(page.GetByTestId("verb-live-state")).ToContainTextAsync("status:in-review");
    }

    /// <summary>
    /// Task 3.3. An operator who sees "cancelled" against a run that finished two minutes ago will believe
    /// they stopped something. The no-op has to read as a refusal, visibly not a success.
    /// </summary>
    [Fact]
    public async Task Cancel_Should_ReportANoOpRatherThanSuccess()
    {
        Fixture.Verbs.Cancel = new DispatchResult(
            VerbOutcome.Refused,
            "Succeeded",
            "That execution is already succeeded; nothing to stop."
        );

        var page = await ArrangeAndGoAsync();
        await page.GetByTestId("cancel-execution").ClickAsync();

        var result = page.GetByTestId("verb-result");
        await Expect(result).ToHaveAttributeAsync("data-outcome", "refused");
        await Expect(result).ToContainTextAsync("nothing to stop");
    }

    /// <summary>The page, not the verb, knows who clicked — so the page is what has to get the attribution
    /// right, and the audit trail is worth nothing if it does not.</summary>
    [Fact]
    public async Task Verb_Should_BeAttributedToTheSignedInUser()
    {
        Fixture.Verbs.Dispatch = new DispatchResult(VerbOutcome.Performed, null, null);

        var page = await ArrangeAndGoAsync();
        await page.GetByTestId("dispatch-item").ClickAsync();
        await Expect(page.GetByTestId("verb-result")).ToBeVisibleAsync();

        Fixture.Verbs.LastActor.ShouldBe("browser@example.com");
    }

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
