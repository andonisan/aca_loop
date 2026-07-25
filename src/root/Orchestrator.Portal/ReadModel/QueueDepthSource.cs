using Azure.Core;
using Azure.Storage.Queues;
using Microsoft.Extensions.Options;
using Orchestrator.Portal.Registry;

namespace Orchestrator.Portal.ReadModel;

/// <summary>
/// A stack's pending dispatch backlog, read as the queue's approximate message count via the portal's
/// managed identity.
/// <para>
/// Approximate is the only thing Azure Storage offers, and it is the right shape anyway: the number is
/// a health signal, not an accounting figure. It also counts messages currently invisible to other
/// readers, so a depth of 1 with a running pod usually means the pod is holding that very message.
/// </para>
/// </summary>
public sealed class QueueDepthSource(TokenCredential credential, IOptions<ReadModelOptions> options)
    : IStackSource<int>
{
    public string Name => "queue-depth";

    public TimeSpan Ttl => options.Value.QueueDepthTtl;

    public async Task<int> ReadAsync(
        RegisteredStack stack,
        CancellationToken cancellationToken = default
    )
    {
        var client = new QueueClient(
            new Uri(stack.Azure.QueueServiceUri, stack.Azure.QueueName),
            credential
        );

        var properties = await client.GetPropertiesAsync(cancellationToken);
        return properties.Value.ApproximateMessagesCount;
    }
}
