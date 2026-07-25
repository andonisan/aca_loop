using Azure.Storage.Queues;
using Orchestrator.Modules.Backlog.Contracts;
using Orchestrator.Modules.Dispatch.Contracts;

namespace Orchestrator.Modules.Dispatch;

sealed class AzureStorageWorkItemQueue(QueueClient queueClient) : IWorkItemQueue
{
    // Serialization lives in Dispatch.Contracts: the portal's manual-dispatch verb is a second writer to
    // this queue, and two copies of the wire format would eventually disagree.
    public async Task EnqueueAsync(
        CanonicalMessage message,
        CancellationToken cancellationToken = default
    ) => await queueClient.SendMessageAsync(QueueMessage.Serialize(message), cancellationToken);
}
