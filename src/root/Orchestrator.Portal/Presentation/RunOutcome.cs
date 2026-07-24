using Orchestrator.Constants;

namespace Orchestrator.Portal.Presentation;

/// <summary>
/// A terminal run outcome as the Portal models it, parsed at the boundary from the wire string that
/// <see cref="LifecycleOutcomes"/> defines and Log Analytics returns.
/// </summary>
/// <remarks>
/// The wire value is a string, so the read model could pass it around as one. It does not, for a
/// reason the design calls out: an unrecognised outcome must be *visible*, not silently styled. A
/// string flows through a mapping and out the other side as "whatever the fallback is"; an enum with an
/// explicit <see cref="Unknown"/> member forces every consumer to have a position on the skew case.
/// </remarks>
enum RunOutcome
{
    /// <summary>Not a real outcome: the wire value did not match any known one, which in practice means
    /// a stack is emitting a schema this Portal build does not understand.</summary>
    Unknown = 0,

    Succeeded,
    Failed,
    StaleGate,
    Poison,
    WipRefused,
}

static class RunOutcomeParser
{
    /// <summary>
    /// Maps a wire outcome to its <see cref="RunOutcome"/>, or <see cref="RunOutcome.Unknown"/> when the
    /// value is absent or unrecognised. Comparison is ordinal and case-sensitive on purpose: the emitter
    /// writes these from the same constants, so a case difference is a contract violation worth
    /// surfacing rather than quietly accepting.
    /// </summary>
    internal static RunOutcome Parse(string? wireValue) =>
        wireValue switch
        {
            LifecycleOutcomes.Succeeded => RunOutcome.Succeeded,
            LifecycleOutcomes.Failed => RunOutcome.Failed,
            LifecycleOutcomes.StaleGate => RunOutcome.StaleGate,
            LifecycleOutcomes.Poison => RunOutcome.Poison,
            LifecycleOutcomes.WipRefused => RunOutcome.WipRefused,
            _ => RunOutcome.Unknown,
        };
}
