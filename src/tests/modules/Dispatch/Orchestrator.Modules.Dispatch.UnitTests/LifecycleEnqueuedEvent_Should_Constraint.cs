using System.Text;
using Microsoft.AspNetCore.Http;
using Orchestrator.Constants;
using Orchestrator.Modules.Backlog.Contracts;
using Orchestrator.Modules.Dispatch.Contracts;
using Shouldly;

namespace Orchestrator.Modules.Dispatch.UnitTests;

/// <summary>
/// The <c>dispatch.enqueued</c> half of the normalized lifecycle contract. What matters beyond the
/// field set is the ordering guarantee: a row must mean the message really reached the queue, so a
/// failing enqueue must produce no event at all.
/// </summary>
public class LifecycleEnqueuedEvent_Should_Constraint
{
    const int EnqueuedEventId = 1400;

    const string MappingYaml = """
        targets:
          - vendor: GitHub
            repo: "acme/widgets"
            states:
              "status:ready-for-proposal":
                action: /ds:propose
                runtime: Claude
                onFailureState: "status:blocked"
        """;

    static readonly CanonicalEvent GatedEvent = new(
        Vendor.GitHub,
        Project: null,
        Repo: "acme/widgets",
        ItemId: "42",
        NativeState: "status:ready-for-proposal",
        Actor: "a-human"
    );

    static HttpContext EmptyBodyContext() =>
        new DefaultHttpContext
        {
            Request = { Body = new MemoryStream(Encoding.UTF8.GetBytes("{}")) },
        };

    static async Task<(
        RecordingLogger<WebhookUseCase> Logger,
        FakeWorkItemQueue Queue
    )> HandleAsync(CanonicalEvent? parsed, FakeWorkItemQueue? queue = null)
    {
        var logger = new RecordingLogger<WebhookUseCase>();
        var workQueue = queue ?? new FakeWorkItemQueue();
        var adapter = new FakeVendorAdapter(Vendor.GitHub) { Parsed = parsed };

        await WebhookUseCase.HandleAsync(
            "github",
            EmptyBodyContext(),
            [adapter],
            new MappingProvider(MappingLoader.Parse(MappingYaml)),
            workQueue,
            logger,
            CancellationToken.None
        );

        return (logger, workQueue);
    }

    static IReadOnlyList<RecordedLog> EnqueuedEvents(RecordingLogger<WebhookUseCase> logger) =>
        [.. logger.Records.Where(record => record.Field("EventName") == LifecycleEvents.Enqueued)];

    [Fact]
    public async Task HandleAsync_Should_EmitOneEnqueuedEventForAGatedItem()
    {
        var (logger, queue) = await HandleAsync(GatedEvent);

        queue.Enqueued.ShouldHaveSingleItem();

        var events = EnqueuedEvents(logger);
        events.ShouldHaveSingleItem();

        var enqueued = events[0];
        enqueued.EventId.ShouldBe(EnqueuedEventId);
        enqueued.Field("SchemaVersion").ShouldBe(LifecycleEvents.SchemaVersion.ToString());
        enqueued.Field("Vendor").ShouldBe(nameof(Vendor.GitHub));
        enqueued.Field("Repo").ShouldBe(GatedEvent.Repo);
        enqueued.Field("ItemId").ShouldBe(GatedEvent.ItemId);
        enqueued.Field("State").ShouldBe(GatedEvent.NativeState);
        enqueued.Field("Action").ShouldBe("/ds:propose");
        enqueued.Field("Runtime").ShouldBe(nameof(AgentRuntime.Claude));
    }

    [Fact]
    public async Task HandleAsync_Should_EmitNoEnqueuedEventForAnUnmappedState()
    {
        var (logger, queue) = await HandleAsync(
            GatedEvent with
            {
                NativeState = "status:in-progress",
            }
        );

        // Dropped as "not a gate state" -- nothing was handed off, so nothing may appear in history.
        queue.Enqueued.ShouldBeEmpty();
        EnqueuedEvents(logger).ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_Should_EmitNoEnqueuedEventWhenTheEnqueueItselfFails()
    {
        var queue = new FakeWorkItemQueue { Throw = true };

        await Should.ThrowAsync<InvalidOperationException>(() => HandleAsync(GatedEvent, queue));

        // The event is emitted after the enqueue precisely so a queue outage cannot leave a row
        // claiming work was dispatched when it never was.
        queue.Enqueued.ShouldBeEmpty();
    }

    sealed class FakeWorkItemQueue : IWorkItemQueue
    {
        public List<CanonicalMessage> Enqueued { get; } = [];
        public bool Throw { get; init; }

        public Task EnqueueAsync(
            CanonicalMessage message,
            CancellationToken cancellationToken = default
        )
        {
            if (Throw)
            {
                throw new InvalidOperationException("queue unavailable");
            }

            Enqueued.Add(message);
            return Task.CompletedTask;
        }
    }

    /// <summary>Tracker-only, which is all Dispatch consumes: the webhook path never mints
    /// credentials, so this fake has no reason to implement <c>ICodeHostAdapter</c>.</summary>
    sealed class FakeVendorAdapter(Vendor vendor) : IBacklogAdapter
    {
        public CanonicalEvent? Parsed { get; init; }

        public Vendor Vendor { get; } = vendor;

        public bool VerifyWebhook(
            ReadOnlyMemory<byte> rawBody,
            IReadOnlyDictionary<string, string> headers
        ) => true;

        public CanonicalEvent? ParseEvent(
            ReadOnlyMemory<byte> rawBody,
            IReadOnlyDictionary<string, string> headers
        ) => Parsed;

        public bool IsAutomationActor(string? actor) => false;

        public Task<string> ReadItemStateAsync(
            string? project,
            string repo,
            string itemId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task TransitionStateAsync(
            string? project,
            string repo,
            string itemId,
            string newState,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task PostCommentAsync(
            string? project,
            string repo,
            string itemId,
            string body,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}
