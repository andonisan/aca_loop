namespace Orchestrator.Portal.ReadModel;

/// <summary>
/// One AI-pod run: a Container Apps Job execution. Deliberately a small projection rather than the ARM
/// resource — the views need four facts, and passing SDK models into Razor would couple the UI to the
/// Azure SDK's shape.
/// </summary>
/// <param name="Name">The execution name, which is also the log pointer the outcome event records.</param>
/// <param name="Duration">Wall time, still counting for a running execution.</param>
public sealed record PodExecution(
    string Name,
    string Status,
    DateTimeOffset? StartedAt,
    TimeSpan? Duration
)
{
    /// <summary>ACA reports running executions as <c>Running</c>; anything else is terminal.</summary>
    public bool IsRunning => string.Equals(Status, "Running", StringComparison.OrdinalIgnoreCase);
}
