using Microsoft.Playwright;
using Orchestrator.Modules.Backlog.Contracts;
using Orchestrator.Portal.Presentation;
using Orchestrator.Portal.ReadModel;
using Shouldly;
using static Microsoft.Playwright.Assertions;

namespace Orchestrator.Portal.BrowserTests;

/// <summary>
/// A stack dashboard's per-panel behaviour: each panel says which of "nothing to show" and "could not
/// look" it is, and one broken source costs only its own panel.
/// </summary>
public class StackPanels_Should_Constraint(PortalBrowserFixture fixture) : BrowserTest(fixture)
{
    const string Stack = PortalBrowserFixture.HealthyStack;

    /// <summary>
    /// Task 2.4. An empty gate is the steady state of a project that is caught up; an unreachable vendor
    /// is an incident. Rendering both as a blank panel would make the healthy case and the broken case
    /// indistinguishable at a glance, which is the glance this page exists to serve.
    /// </summary>
    [Fact]
    public async Task Panels_Should_DistinguishDegradedFromEmpty()
    {
        ArrangeHealthy();
        Fixture.Panels.Available<IReadOnlyList<BacklogItem>>(GatedItems, Stack, []);
        Fixture.Panels.Failing<RunHistory>(History7d, Stack, "workspace unreachable");

        var page = await GoToStackAsync();

        var empty = page.GetByTestId("panel-gated");
        var failed = page.GetByTestId("panel-history");

        await Expect(empty.GetByTestId("panel-empty")).ToBeVisibleAsync();
        await Expect(empty.GetByTestId("panel-unavailable")).ToHaveCountAsync(0);

        await Expect(failed.GetByTestId("panel-unavailable")).ToBeVisibleAsync();
        await Expect(failed.GetByTestId("panel-empty")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Task 2.5. Every panel reads independently precisely so that one unreachable source does not take
    /// the page with it — a claim about composition that only holds once rendered.
    /// </summary>
    [Fact]
    public async Task Panels_Should_SurviveASingleFailingSource()
    {
        ArrangeHealthy();
        Fixture.Panels.Failing<int>(QueueDepth, Stack, "queue unreachable");

        var page = await GoToStackAsync();

        await Expect(page.GetByTestId("panel-queue").GetByTestId("panel-unavailable"))
            .ToBeVisibleAsync();

        // Exactly one panel is degraded. Asserting the other three individually rather than counting
        // banners, so a failure names which panel gave up.
        foreach (var panel in (string[])["panel-pods", "panel-gated", "panel-history"])
        {
            await Expect(page.GetByTestId(panel).GetByTestId("panel-unavailable"))
                .ToHaveCountAsync(0);
        }
    }

    /// <summary>
    /// Task 2.6. The design system's floor: status must never be carried by colour alone. Asserted
    /// against what a browser actually renders, because a stylesheet review cannot tell you whether the
    /// label element survived into the markup.
    /// </summary>
    [Fact]
    public async Task StatusBadges_Should_CarryATextLabel()
    {
        ArrangeHealthy();

        var page = await GoToStackAsync();

        var badges = page.GetByTestId("status-badge");
        var count = await badges.CountAsync();

        // Without this the loop below would pass on a page that rendered no badges at all.
        count.ShouldBeGreaterThan(0, "the arranged page should render at least one status badge");

        for (var i = 0; i < count; i++)
        {
            var text = await badges.Nth(i).InnerTextAsync();
            text.Trim().ShouldNotBeEmpty($"status badge {i} renders colour with no label");
        }
    }

    /// <summary>Every source available, so a test only has to arrange the one it is about. Without this
    /// the stub's unarranged default (failing) would make every other panel degraded too, and an
    /// assertion about isolation would have nothing healthy to compare against.</summary>
    void ArrangeHealthy()
    {
        Fixture.Panels.Available(QueueDepth, Stack, 7);
        Fixture.Panels.Available<IReadOnlyList<PodExecution>>(
            PodExecutions,
            Stack,
            [
                new PodExecution(
                    "exec-1",
                    PodExecutionState.Running,
                    "Running",
                    new DateTimeOffset(2026, 7, 25, 11, 0, 0, TimeSpan.Zero),
                    TimeSpan.FromMinutes(4)
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
                    "someone",
                    new DateTimeOffset(2026, 7, 25, 10, 0, 0, TimeSpan.Zero),
                    null
                ),
            ]
        );
        Fixture.Panels.Available(
            History7d,
            Stack,
            new RunHistory(
                TimeSpan.FromDays(7),
                [
                    new RunHistoryEntry(
                        new DateTimeOffset(2026, 7, 25, 9, 0, 0, TimeSpan.Zero),
                        "GitHub",
                        "example/alpha",
                        "41",
                        "/ds:implement",
                        "Claude",
                        "succeeded",
                        TimeSpan.FromMinutes(6),
                        "exec-0"
                    ),
                ],
                TotalRuns: 1,
                Succeeded: 1
            )
        );
    }

    async Task<IPage> GoToStackAsync()
    {
        var page = await Fixture.NewPageAsync();
        var response = await page.GotoAsync($"/stacks/{Stack}");

        response!.Ok.ShouldBeTrue();
        return page;
    }
}
