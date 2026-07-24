using System.Text.Json;
using Azure.Storage.Queues;
using Orchestrator.Modules.Backlog.Contracts;
using Orchestrator.Modules.Dispatch.Contracts;

namespace Orchestrator.Modules.Dispatch;

sealed class AzureStorageWorkItemQueue(QueueClient queueClient) : IWorkItemQueue
{
    static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public async Task EnqueueAsync(
        CanonicalMessage message,
        CancellationToken cancellationToken = default
    )
    {
        var body = JsonSerializer.Serialize(message, SerializerOptions);
        await queueClient.SendMessageAsync(body, cancellationToken);
    }
}
