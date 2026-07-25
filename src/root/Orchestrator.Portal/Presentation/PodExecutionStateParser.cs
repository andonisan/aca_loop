namespace Orchestrator.Portal.Presentation;

/// <summary>
/// Maps the ACA management API's execution status onto <see cref="PodExecutionState"/> — the sibling of
/// <c>RunOutcomeParser</c>, for the one status the portal reads from Azure rather than from its own
/// lifecycle events.
/// </summary>
static class PodExecutionStateParser
{
    /// <summary>
    /// Comparison is case-insensitive here, unlike <c>RunOutcomeParser</c>'s deliberate case-sensitivity.
    /// The reason is whose contract it is: outcome strings come from this product's own constants, so a
    /// case difference there is a contract violation worth surfacing. These come from Azure, whose
    /// casing this product does not control and should not fail on.
    /// </summary>
    internal static PodExecutionState Parse(string? status) =>
        status?.ToLowerInvariant() switch
        {
            "running" => PodExecutionState.Running,
            "succeeded" => PodExecutionState.Succeeded,
            "failed" => PodExecutionState.Failed,
            "stopped" => PodExecutionState.Stopped,
            _ => PodExecutionState.Unknown,
        };
}
