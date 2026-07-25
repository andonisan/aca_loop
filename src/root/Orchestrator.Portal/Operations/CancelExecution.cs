using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Orchestrator.Portal.Registry;

namespace Orchestrator.Portal.Operations;

/// <summary>
/// Stops a running AI-pod execution.
/// <para>
/// What this does <b>not</b> do is the part worth understanding. Stopping the execution kills the pod; it
/// does not return the message to the queue, does not move the item's state, and does not run the failure
/// contract. The item stays exactly where it is, which means it stays in its gate state — and a gate state
/// is what dispatches. Whether that redispatches depends on the message's visibility timeout, and this
/// verb deliberately does not pretend to know: it reports the semantics rather than asserting an outcome
/// it cannot guarantee.
/// </para>
/// </summary>
sealed class CancelExecution(TokenCredential credential, ILogger<CancelExecution> logger)
{
    public async Task<DispatchResult> CancelAsync(
        RegisteredStack stack,
        string executionName,
        string actor,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var arm = new ArmClient(credential, stack.Azure.SubscriptionId);
            var execution = arm.GetContainerAppJobExecutionResource(
                ContainerAppJobExecutionResource.CreateResourceIdentifier(
                    stack.Azure.SubscriptionId,
                    stack.Azure.ResourceGroup,
                    stack.Azure.JobName,
                    executionName
                )
            );

            var current = await execution.GetAsync(cancellationToken);
            var state = current.Value.Data.Status?.ToString() ?? "";

            // Cancelling something already finished is a no-op, and saying so is more useful than
            // reporting success: an operator who sees "cancelled" on a run that succeeded two minutes ago
            // will believe they stopped something.
            if (!string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase))
            {
                Audit(actor, stack, executionName, VerbOutcome.Refused, $"already {state}");
                return new DispatchResult(
                    VerbOutcome.Refused,
                    state,
                    $"That execution is already {state.ToLowerInvariant()}; nothing to stop."
                );
            }

            await execution.StopExecutionJobAsync(Azure.WaitUntil.Completed, cancellationToken);

            Audit(actor, stack, executionName, VerbOutcome.Performed, null);
            return new DispatchResult(
                VerbOutcome.Performed,
                state,
                "Execution stopped. The item's state was not changed, so it remains at its gate."
            );
        }
        catch (Exception exception)
        {
            Audit(actor, stack, executionName, VerbOutcome.Failed, exception.Message);
            return new DispatchResult(VerbOutcome.Failed, null, exception.Message);
        }
    }

    void Audit(
        string actor,
        RegisteredStack stack,
        string executionName,
        VerbOutcome outcome,
        string? detail
    ) =>
        PortalLog.VerbPerformed(
            logger,
            "cancel-execution",
            actor,
            stack.Id,
            executionName,
            outcome.ToString().ToLowerInvariant(),
            detail
        );
}
