using Orchestrator.Portal.Operations;
using Orchestrator.Portal.Registry;

namespace Orchestrator.Portal.BrowserTests;

/// <summary>
/// Hands the page a chosen verb outcome.
/// <para>
/// What this suite can and cannot claim is worth being precise about. Substituting the verb means these
/// tests assert <b>how an outcome renders</b>, not how the verb decides on one — that a refusal is visibly
/// a refusal and carries the live state, not that <see cref="ManualDispatch"/> was right to refuse. The
/// decision is the implementation's own claim and belongs to a unit test against it.
/// </para>
/// <para>
/// That split is the point rather than a compromise: the rendering claim is the one no other kind of test
/// can hold, and the deciding claim is the one a browser adds nothing to. Driving the real verb here would
/// also mean reaching Azure and a vendor, which would make the suite slow, flaky and dependent on
/// credentials it has no business holding.
/// </para>
/// </summary>
sealed class StubVerbs : IManualDispatch, ICancelExecution
{
    /// <summary>Defaults to Failed rather than Performed, on the same principle as the panel stub: a test
    /// that forgot to arrange an outcome should see an obviously wrong page, not a plausible success.</summary>
    public DispatchResult Dispatch { get; set; } =
        new(VerbOutcome.Failed, null, "no dispatch outcome arranged");

    public DispatchResult Cancel { get; set; } =
        new(VerbOutcome.Failed, null, "no cancel outcome arranged");

    /// <summary>What the page passed as the actor, so a test can check the audit attribution the page is
    /// responsible for — the verb records it, but only the page knows who clicked.</summary>
    public string? LastActor { get; private set; }

    public Task<DispatchResult> DispatchAsync(
        RegisteredStack stack,
        string itemId,
        string actor,
        CancellationToken cancellationToken = default
    )
    {
        LastActor = actor;
        return Task.FromResult(Dispatch);
    }

    public Task<DispatchResult> CancelAsync(
        RegisteredStack stack,
        string executionName,
        string actor,
        CancellationToken cancellationToken = default
    )
    {
        LastActor = actor;
        return Task.FromResult(Cancel);
    }

    public void Reset()
    {
        Dispatch = new DispatchResult(VerbOutcome.Failed, null, "no dispatch outcome arranged");
        Cancel = new DispatchResult(VerbOutcome.Failed, null, "no cancel outcome arranged");
        LastActor = null;
    }
}
