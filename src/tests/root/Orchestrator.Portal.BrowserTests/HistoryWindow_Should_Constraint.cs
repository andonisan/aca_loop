using Microsoft.Playwright;
using Orchestrator.Portal.ReadModel;
using Shouldly;
using static Microsoft.Playwright.Assertions;

namespace Orchestrator.Portal.BrowserTests;

/// <summary>
/// Task 3.1. The history window selector is the portal's only stateful control, and it is the one thing
/// in the read model that a request-level test genuinely cannot reach: changing the window re-reads a
/// different source and re-renders in place, over the interactive circuit, with no navigation and no new
/// HTTP request to assert against.
/// </summary>
public class HistoryWindow_Should_Constraint(PortalBrowserFixture fixture) : BrowserTest(fixture)
{
    const string Stack = PortalBrowserFixture.HealthyStack;

    /// <summary>The name <c>RunHistorySource</c> reports for the one-day window — the same coupling, and
    /// the same loud failure on mismatch, as <see cref="BrowserTest.History7d"/>.</summary>
    const string History1d = "run-history::1440m";

    [Fact]
    public async Task Selector_Should_ReReadAgainstTheChosenWindow()
    {
        // Two windows holding deliberately different totals, so "it re-read" and "it re-rendered the same
        // numbers" cannot be confused for each other.
        Fixture.Panels.Available(
            History7d,
            Stack,
            new RunHistory(TimeSpan.FromDays(7), [], TotalRuns: 40, Succeeded: 20)
        );
        Fixture.Panels.Available(
            History1d,
            Stack,
            new RunHistory(TimeSpan.FromDays(1), [], TotalRuns: 4, Succeeded: 4)
        );

        var page = await Fixture.NewPageAsync();
        var response = await page.GotoAsync($"/stacks/{Stack}");
        response!.Ok.ShouldBeTrue();

        var history = page.GetByTestId("panel-history");

        // The seven-day window is the default, so this is what the page loads with.
        await Expect(history).ToContainTextAsync("50%");
        await Expect(history).ToContainTextAsync("40 runs");

        await page.GetByLabel("History window").SelectOptionAsync("1");

        // Re-read and re-rendered in place. ToContainTextAsync retries until the circuit pushes the new
        // markup, so this does not need a sleep and does not race the round trip.
        await Expect(history).ToContainTextAsync("100%");
        await Expect(history).ToContainTextAsync("4 runs");
    }
}
