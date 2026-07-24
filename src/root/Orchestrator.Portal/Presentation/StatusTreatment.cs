using Orchestrator.Constants;

namespace Orchestrator.Portal.Presentation;

/// <summary>A status's visual treatment: the design system's badge modifier plus the text that must
/// accompany it.</summary>
/// <remarks>
/// The label travels with the class deliberately. The design system requires that status is never
/// communicated by colour alone, and a helper returning a bare class name would let a caller satisfy
/// the type while rendering an empty coloured pill. Pairing them makes the accessible rendering the
/// only convenient one.
/// </remarks>
/// <param name="CssClass">Badge modifier — one of the design system's <c>ok/warn/danger/info/neutral/
/// violet/orange/yellow</c>.</param>
/// <param name="Label">Human-readable state text. Never empty.</param>
/// <remarks>Public only because it is a Razor component parameter type, which the generated component
/// exposes publicly; nothing outside this host consumes it.</remarks>
public record StatusBadge(string CssClass, string Label);

/// <summary>
/// The single definition of how orchestrator state looks. Pages ask this for a treatment instead of
/// choosing a colour, so the same concept reads the same everywhere (design D4).
/// </summary>
static class StatusTreatment
{
    /// <summary>
    /// Treatment for a terminal run outcome.
    /// </summary>
    /// <remarks>
    /// The two judgements worth stating, because both are the kind that get "simplified" later:
    /// <list type="bullet">
    /// <item><description><see cref="RunOutcome.StaleGate"/> is <c>warn</c>, not <c>danger</c>. The run
    /// correctly declined to act — the item left its gate state before the pod started. Presenting it as
    /// an error trains operators to ignore red.</description></item>
    /// <item><description><see cref="RunOutcome.Poison"/> gets its own hue rather than sharing
    /// <c>danger</c> with <see cref="RunOutcome.Failed"/>. It is categorically different: redelivery
    /// gave up and the work was dropped unrun. Blended into the failure count it becomes invisible,
    /// which is the opposite of what a dropped message needs.</description></item>
    /// </list>
    /// <see cref="RunOutcome.WipRefused"/> is <c>neutral</c>: the command ran and declined because the
    /// repo was at its WIP limit, keeping the gate state. That is backpressure working as designed, so
    /// it should be visible but must not compete with real problems for attention.
    /// </remarks>
    internal static StatusBadge ForOutcome(RunOutcome outcome) =>
        outcome switch
        {
            RunOutcome.Succeeded => new("ok", "succeeded"),
            RunOutcome.Failed => new("danger", "failed"),
            RunOutcome.StaleGate => new("warn", "stale gate"),
            RunOutcome.Poison => new("violet", "poison"),
            RunOutcome.WipRefused => new("neutral", "WIP refused"),

            // Not a fallback for convenience — a stack emitting an outcome this build does not know is a
            // schema skew, and it says so rather than picking a colour and moving on.
            RunOutcome.Unknown => new("orange", "unknown outcome"),

            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unmapped run outcome."),
        };

    /// <summary>Treatment for a wire outcome string, parsing it first.</summary>
    internal static StatusBadge ForOutcome(string? wireValue) => ForOutcome(RunOutcomeParser.Parse(wireValue));

    /// <summary>
    /// Treatment for an ACA job execution's state. <c>Running</c> is <c>info</c> rather than a success or
    /// warning hue because in-flight is the expected steady state for a busy stack, and a dashboard where
    /// normal operation is green offers nowhere to escalate to.
    /// </summary>
    internal static StatusBadge ForExecution(PodExecutionState state) =>
        state switch
        {
            PodExecutionState.Running => new("info", "running"),
            PodExecutionState.Succeeded => new("ok", "succeeded"),
            PodExecutionState.Failed => new("danger", "failed"),

            // Operator-initiated (the cancel verb) or platform-initiated. Either way an expected end
            // state, not a fault.
            PodExecutionState.Stopped => new("neutral", "stopped"),

            PodExecutionState.Unknown => new("orange", "unknown state"),

            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unmapped execution state."),
        };

    /// <summary>
    /// Treatment for a queue depth, against thresholds supplied by the caller.
    /// </summary>
    /// <remarks>
    /// Thresholds are parameters rather than constants because the real numbers come from the stack's
    /// own KEDA scale rule, which this Portal does not read yet (blocked on the queue read model,
    /// <c>orchestrator-portal</c> task 3.1). Hard-coding placeholders here would bake a guess into the
    /// one place the design says must not be guessed at per-page.
    /// </remarks>
    /// <param name="depth">Approximate message count.</param>
    /// <param name="warnAt">Depth at or above which the queue is building faster than it drains.</param>
    /// <param name="dangerAt">Depth at or above which scaling cannot keep up.</param>
    internal static StatusBadge ForQueueDepth(int depth, int warnAt, int dangerAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        ArgumentOutOfRangeException.ThrowIfNegative(warnAt);

        // An inverted pair would silently disable the danger band, so it is rejected rather than
        // normalised: the thresholds arrive from configuration and a swapped pair is a config bug.
        ArgumentOutOfRangeException.ThrowIfLessThan(dangerAt, warnAt);

        return depth >= dangerAt ? new("danger", $"{depth} queued")
            : depth >= warnAt ? new("warn", $"{depth} queued")
            : new("ok", $"{depth} queued");
    }
}

/// <summary>An ACA job execution's state as the Portal models it, parsed at the boundary from whatever
/// the ACA management API returns.</summary>
enum PodExecutionState
{
    Unknown = 0,
    Running,
    Succeeded,
    Failed,
    Stopped,
}
