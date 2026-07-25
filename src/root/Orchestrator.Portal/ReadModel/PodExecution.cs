using Orchestrator.Portal.Presentation;

namespace Orchestrator.Portal.ReadModel;

/// <summary>
/// One AI-pod run: a Container Apps Job execution. Deliberately a small projection rather than the ARM
/// resource — the views need four facts, and passing SDK models into Razor would couple the UI to the
/// Azure SDK's shape.
/// <para>
/// <paramref name="State"/> is already parsed rather than a raw status string: this source is the
/// boundary <see cref="PodExecutionState"/> documents itself as being parsed at, so parsing here means no
/// view can receive a status it has to interpret.
/// </para>
/// </summary>
/// <param name="Name">The execution name, which is also the log pointer the outcome event records.</param>
/// <param name="RawStatus">What Azure actually said, kept for the badge's hover title so an
/// <see cref="PodExecutionState.Unknown"/> is diagnosable rather than opaque.</param>
/// <param name="Duration">Wall time, still counting for a running execution.</param>
sealed record PodExecution(
    string Name,
    PodExecutionState State,
    string RawStatus,
    DateTimeOffset? StartedAt,
    TimeSpan? Duration
)
{
    public bool IsRunning => State == PodExecutionState.Running;
}
