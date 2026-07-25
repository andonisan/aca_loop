using Orchestrator.Constants;

namespace Orchestrator.Portal.BrowserTests;

/// <summary>
/// Shared setup for every browser test: one host and one browser for the suite, fresh stub state per test
/// (design D5).
/// <para>
/// The reset lives in a base constructor rather than in each test because xUnit builds a new instance per
/// test, so this runs exactly once before each one and cannot be forgotten. Forgetting it would not fail
/// loudly — it would leak one test's arrangement into the next and produce a pass that depends on
/// execution order.
/// </para>
/// </summary>
[Collection(BrowserCollection.Name)]
public abstract class BrowserTest
{
    /// <summary>The name <c>RunHistorySource</c> reports for the seven-day window the pages request by
    /// default. Coupled to that source's <c>Name</c> format deliberately: the stub is keyed by name, so
    /// this has to move when the format does. A mismatch fails loudly rather than quietly — an unarranged
    /// source reads as unavailable, so any test expecting data would go red.</summary>
    protected const string History7d = "run-history::10080m";

    protected const string QueueDepth = "queue-depth";
    protected const string PodExecutions = "pod-executions";
    protected const string GatedItems = "gated-items";

    protected BrowserTest(PortalBrowserFixture fixture)
    {
        Fixture = fixture;
        fixture.Panels.Reset();
        fixture.Verbs.Reset();
        fixture.Roles = [PortalRoles.Operator];
    }

    protected PortalBrowserFixture Fixture { get; }
}
