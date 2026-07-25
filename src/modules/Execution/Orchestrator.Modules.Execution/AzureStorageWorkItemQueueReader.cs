using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Storage.Queues;
using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.Execution;

sealed class AzureStorageWorkItemQueueReader(QueueClient queueClient) : IWorkItemQueueReader
{
    static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<ReceivedWorkItem?> ReceiveAsync(
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default
    )
    {
        var response = await queueClient.ReceiveMessagesAsync(
            maxMessages: 1,
            visibilityTimeout: visibilityTimeout,
            cancellationToken: cancellationToken
        );

        var message = response.Value.FirstOrDefault();
        if (message is null)
        {
            return null;
        }

        var payload = JsonSerializer.Deserialize<CanonicalMessage>(
            message.MessageText,
            SerializerOptions
        )!;

        return new ReceivedWorkItem(
            message.MessageId,
            message.PopReceipt,
            message.DequeueCount,
            payload
        );
    }

    public Task DeleteAsync(ReceivedWorkItem item, CancellationToken cancellationToken = default) =>
        queueClient.DeleteMessageAsync(item.MessageId, item.PopReceipt, cancellationToken);
}
