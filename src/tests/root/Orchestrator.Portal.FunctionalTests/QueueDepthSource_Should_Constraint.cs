using Azure.Storage.Queues;
using Microsoft.Extensions.Options;
using Orchestrator.Modules.Backlog.Contracts;
using Orchestrator.Portal.ReadModel;
using Orchestrator.Portal.Registry;
using Shouldly;

namespace Orchestrator.Portal.FunctionalTests;

/// <summary>
/// <see cref="QueueDepthSource"/> against Azurite — Microsoft's own emulator, speaking the real Queue
/// Storage protocol rather than a hand-written fake of it. That distinction is the whole point: the
/// uncertain part of this source was never its three lines of C#, it was whether the API being called means
/// what the code assumes it means.
/// <para>
/// Specifically, the source's doc comment claims the approximate count includes messages currently held
/// invisible by a reader — the sentence that makes "depth 1 with a running pod" readable as "the pod is
/// holding that message" rather than "there is unclaimed work". That was belief when written; these pin it.
/// </para>
/// </summary>
public sealed class QueueDepthSource_Should_Constraint(AzuriteFixture azurite)
    : IClassFixture<AzuriteFixture>
{
    static QueueDepthSource SourceOver(QueueClient queue) =>
        // Azurite authenticates with a shared key, not a token, which is exactly why the source takes a
        // client factory rather than constructing the client itself.
        new(_ => queue, Options.Create(new ReadModelOptions()));

    static readonly RegisteredStack Stack = new(
        "dsconnect-dev",
        "ds-connect (dev)",
        "mappings/dsconnect-dev.mapping.yaml",
        new StackAzureCoordinates(
            "sub",
            "rg",
            new Uri("https://acct.queue.core.windows.net"),
            "ai-jobs",
            "agent-job",
            "workspace"
        ),
        new StackVendorCoordinates(Vendor.GitHub, null, "acme/widgets", "Portal--Read")
    );

    [Fact]
    public async Task ReadAsync_Should_ReturnZeroForAnEmptyQueue()
    {
        var queue = await azurite.CreateQueueAsync();

        (await SourceOver(queue).ReadAsync(Stack)).ShouldBe(0);
    }

    [Fact]
    public async Task ReadAsync_Should_CountEnqueuedMessages()
    {
        var queue = await azurite.CreateQueueAsync();
        for (var i = 0; i < 3; i++)
        {
            await queue.SendMessageAsync($"message-{i}");
        }

        (await SourceOver(queue).ReadAsync(Stack)).ShouldBe(3);
    }

    [Fact]
    public async Task ReadAsync_Should_StillCountAMessageAnAgentIsHolding()
    {
        var queue = await azurite.CreateQueueAsync();
        await queue.SendMessageAsync("in-flight");

        // Exactly what a running agent replica does: receive the message, making it invisible to other
        // readers for the visibility timeout while it works.
        var received = await queue.ReceiveMessageAsync(TimeSpan.FromMinutes(5));
        received.Value.ShouldNotBeNull();

        // The claim under test. If this returned 0, "queue depth" would mean "unclaimed work", and the
        // dashboard's queue panel would read as empty while a pod was mid-run.
        (await SourceOver(queue).ReadAsync(Stack)).ShouldBe(1);
    }

    [Fact]
    public async Task ReadAsync_Should_StopCountingADeletedMessage()
    {
        var queue = await azurite.CreateQueueAsync();
        await queue.SendMessageAsync("done-with-this");
        var received = await queue.ReceiveMessageAsync(TimeSpan.FromMinutes(5));

        await queue.DeleteMessageAsync(received.Value.MessageId, received.Value.PopReceipt);

        // The agent deletes on every terminal path, so a drained queue must read as drained.
        (await SourceOver(queue).ReadAsync(Stack)).ShouldBe(0);
    }
}
