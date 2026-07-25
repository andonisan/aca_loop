using Orchestrator.Modules.Backlog.Contracts;
using Orchestrator.Modules.Dispatch.Contracts;
using Orchestrator.Portal.ReadModel;
using Orchestrator.Portal.Registry;

namespace Orchestrator.Portal.Operations;

/// <param name="Outcome">What actually happened, in the closed set the audit trail records.</param>
/// <param name="LiveState">The state the item was really in when checked. Present on a refusal, because
/// "refused" without it tells the operator nothing they can act on.</param>
public sealed record DispatchResult(VerbOutcome Outcome, string? LiveState, string? Detail);

/// <summary>The closed set of verb outcomes. Every verb path ends in exactly one, so an audit trail with a
/// gap means a path was added without one.</summary>
public enum VerbOutcome
{
    Performed,
    Refused,
    Failed,
}

/// <summary>
/// The dispatch verb, behind an interface for the same reason <c>IPanelReader</c> is: a page's rendering
/// of an outcome is a separate claim from the verb's decision to reach it, and only one of the two can be
/// checked in a browser. The decision belongs to a unit test against the implementation below.
/// </summary>
interface IManualDispatch
{
    Task<DispatchResult> DispatchAsync(
        RegisteredStack stack,
        string itemId,
        string actor,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Manual dispatch: re-enqueue an item that is genuinely sitting at a gate.
/// <para>
/// The gate invariant is the whole product's concurrency lock — an item dispatches because it is in a
/// mapped state, and the agent re-validates that before doing anything. A manual verb that could enqueue
/// an item in some other state would put a message on the queue that the agent then discards as a stale
/// gate: wasted, confusing, and an operator convinced they did something. So this checks the live state
/// first and refuses out loud.
/// </para>
/// <para>
/// It writes nothing to the vendor. Moving an item into a gate state stays a human action on the board,
/// which is the product's authorization gate; this verb only re-runs what the board already says.
/// </para>
/// </summary>
sealed class ManualDispatch(
    BacklogReaderFactory readers,
    StackQueueClientFactory queueFactory,
    ILogger<ManualDispatch> logger
) : IManualDispatch
{
    public async Task<DispatchResult> DispatchAsync(
        RegisteredStack stack,
        string itemId,
        string actor,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var mapping = MappingLoader.LoadFromFile(stack.MappingFilePath);
            var vendor = stack.Vendor;

            // Read-only client, deliberately: this verb must not be able to write to the vendor even by
            // accident, and using the reader rather than the adapter makes that structural (design D9).
            var liveState = await readers
                .For(stack)
                .ReadItemStateAsync(vendor.Project, vendor.Repo, itemId, cancellationToken);

            // Resolved through the same mapping the receiver uses, so the portal cannot dispatch on a
            // state that stack would not dispatch on.
            if (
                !new MappingProvider(mapping).TryResolve(
                    vendor.Vendor,
                    vendor.Project,
                    vendor.Repo,
                    liveState,
                    out var resolved
                )
            )
            {
                Audit(actor, stack, itemId, VerbOutcome.Refused, $"live state '{liveState}'");
                return new DispatchResult(
                    VerbOutcome.Refused,
                    liveState,
                    string.IsNullOrEmpty(liveState)
                        ? "The item carries no lifecycle state."
                        : $"'{liveState}' is not a gate state for this stack."
                );
            }

            var message = new CanonicalMessage(
                vendor.Vendor,
                vendor.Project,
                vendor.Repo,
                itemId,
                liveState,
                resolved!.State.Action,
                resolved.State.Runtime.ToString(),
                resolved.State.OnFailureState,
                resolved.Target.CodeHost,
                resolved.Target.CodeRepo
            );

            await queueFactory(stack)
                .SendMessageAsync(QueueMessage.Serialize(message), cancellationToken);

            Audit(actor, stack, itemId, VerbOutcome.Performed, resolved.State.Action);
            return new DispatchResult(VerbOutcome.Performed, liveState, resolved.State.Action);
        }
        catch (Exception exception)
        {
            // Failed, not refused. A refusal is the system working -- the gate did not hold. A failure is
            // the system not working, and conflating them would make the audit trail useless for telling
            // "we declined" from "we broke".
            Audit(actor, stack, itemId, VerbOutcome.Failed, exception.Message);
            return new DispatchResult(VerbOutcome.Failed, null, exception.Message);
        }
    }

    void Audit(
        string actor,
        RegisteredStack stack,
        string itemId,
        VerbOutcome outcome,
        string? detail
    ) =>
        PortalLog.VerbPerformed(
            logger,
            "manual-dispatch",
            actor,
            stack.Id,
            itemId,
            outcome.ToString().ToLowerInvariant(),
            detail
        );
}
