using Azure.Identity;
using Azure.Storage.Queues;
using Microsoft.Extensions.Options;

namespace Orchestrator.Modules.Execution;

static class QueueClientFactory
{
    public static QueueClient Create(IOptions<QueueOptions> options)
    {
        var o = options.Value;
        var clientOptions = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.None };

        return !string.IsNullOrWhiteSpace(o.ConnectionString)
            ? new QueueClient(o.ConnectionString, o.QueueName, clientOptions)
            : new QueueClient(
                new Uri($"https://{o.AccountName}.queue.core.windows.net/{o.QueueName}"),
                new DefaultAzureCredential(),
                clientOptions
            );
    }
}
