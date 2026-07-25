namespace Orchestrator.Portal.ReadModel;

/// <param name="LogPointer">The ACA job execution name; see
/// <c>LifecycleOutcomes.LogPointerEnvironmentVariable</c> for how to get from it to that run's console
/// logs.</param>
public sealed record RunHistoryEntry(
    DateTimeOffset At,
    string Vendor,
    string? Repo,
    string ItemId,
    string Action,
    string Runtime,
    string Outcome,
    TimeSpan Duration,
    string LogPointer
);

/// <summary>
/// A stack's run history over a window. The counts come from their own aggregate query rather than being
/// derived from <paramref name="Runs"/>, because that list is capped for display — computing a success
/// rate from a truncated sample would quietly report the rate of the most recent N runs as the rate of
/// the window.
/// </summary>
/// <param name="Window">Labelled in the UI as workspace retention, which is the real bound on how far
/// back this can see (portal-read-model spec).</param>
public sealed record RunHistory(
    TimeSpan Window,
    IReadOnlyList<RunHistoryEntry> Runs,
    int TotalRuns,
    int Succeeded
)
{
    /// <summary>Null when the window contains no runs — a rate of zero would read as "everything
    /// failed" rather than "nothing ran".</summary>
    public double? SuccessRate => TotalRuns == 0 ? null : (double)Succeeded / TotalRuns;
}
