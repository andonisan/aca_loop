using Azure.Core;
using Azure.Storage.Queues;
using Microsoft.Extensions.Options;
using Orchestrator.Portal.Registry;

namespace Orchestrator.Portal.ReadModel;

/// <summary>How this portal reaches a stack's queue. A seam, not an abstraction for its own sake: in Azure
/// this is the stack's HTTPS endpoint plus the portal's managed identity, and in a test it is Azurite,
/// which speaks the real Queue protocol but authenticates with a shared key rather than a token.</summary>
delegate QueueClient StackQueueClientFactory(RegisteredStack stack);

/// <summary>
/// A stack's pending dispatch backlog, read as the queue's approximate message count via the portal's
/// managed identity.
/// <para>
/// Approximate is the only thing Azure Storage offers, and it is the right shape anyway: the number is
/// a health signal, not an accounting figure. It also counts messages currently invisible to other
/// readers — so a depth of 1 with a running pod usually means the pod is holding that very message.
/// That last claim is not folklore: it is pinned by a test against Azurite.
/// </para>
/// </summary>
sealed class QueueDepthSource(
    StackQueueClientFactory clientFactory,
    IOptions<ReadModelOptions> options
) : IStackSource<int>
{
    public string Name => "queue-depth";

    public TimeSpan Ttl => options.Value.QueueDepthTtl;

    public async Task<int> ReadAsync(
        RegisteredStack stack,
        CancellationToken cancellationToken = default
    )
    {
        var properties = await clientFactory(stack).GetPropertiesAsync(cancellationToken);
        return properties.Value.ApproximateMessagesCount;
    }
}

static class StackQueueClient
{
    /// <summary>The production factory: the stack's own queue endpoint, read with the portal's identity.
    /// The portal never holds a stack's storage keys.</summary>
    internal static StackQueueClientFactory ForCredential(TokenCredential credential) =>
        stack => new QueueClient(
            new Uri(stack.Azure.QueueServiceUri, stack.Azure.QueueName),
            credential
        );
}
