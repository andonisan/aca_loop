using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Microsoft.Extensions.Options;
using Orchestrator.Portal.Registry;

namespace Orchestrator.Portal.ReadModel;

/// <summary>
/// A stack's AI-pod executions — running first, then most recently started — read from the Container
/// Apps job via the portal's managed identity.
/// <para>
/// Capped rather than paginated: this is a live-state panel answering "what is happening right now",
/// and anything older belongs to the history view, which reads Log Analytics and is not bounded by what
/// ACA still retains. Taking a fixed number also bounds the ARM paging cost of a page that polls.
/// </para>
/// </summary>
public sealed class PodExecutionsSource(
    TokenCredential credential,
    TimeProvider timeProvider,
    IOptions<ReadModelOptions> options
) : IStackSource<IReadOnlyList<PodExecution>>
{
    public string Name => "pod-executions";

    public TimeSpan Ttl => options.Value.ExecutionsTtl;

    public async Task<IReadOnlyList<PodExecution>> ReadAsync(
        RegisteredStack stack,
        CancellationToken cancellationToken = default
    )
    {
        var arm = new ArmClient(credential, stack.Azure.SubscriptionId);
        var job = arm.GetContainerAppJobResource(
            ContainerAppJobResource.CreateResourceIdentifier(
                stack.Azure.SubscriptionId,
                stack.Azure.ResourceGroup,
                stack.Azure.JobName
            )
        );

        var now = timeProvider.GetUtcNow();
        var executions = new List<PodExecution>();

        await foreach (
            var execution in job.GetContainerAppJobExecutions()
                .GetAllAsync(filter: null, cancellationToken)
                .WithCancellation(cancellationToken)
        )
        {
            var data = execution.Data;
            var startedAt = data.StartOn;
            var endedAt = data.EndOn;

            executions.Add(
                new PodExecution(
                    data.Name,
                    data.Status?.ToString() ?? "Unknown",
                    startedAt,
                    startedAt is null ? null : (endedAt ?? now) - startedAt
                )
            );

            if (executions.Count >= options.Value.RecentExecutions)
            {
                break;
            }
        }

        // Running first: an operator opening this panel is looking for what is in flight, and ACA does
        // not guarantee an ordering.
        return
        [
            .. executions
                .OrderByDescending(e => e.IsRunning)
                .ThenByDescending(e => e.StartedAt ?? DateTimeOffset.MinValue),
        ];
    }
}
