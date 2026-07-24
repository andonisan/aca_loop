using Orchestrator.Constants;
using Orchestrator.Modules.Backlog.Contracts;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Orchestrator.Modules.Execution.UnitTests;

/// <summary>
/// The normalized <c>dispatch.outcome</c> event is a queried contract, not incidental logging: the
/// Portal's run history is only as complete as this event's coverage. These tests pin the
/// "exactly one terminal event per execution that dequeued" invariant on every path out of
/// <see cref="ExecutionService.RunOnceAsync"/> — a new early return that forgets to emit fails here.
/// </summary>
public class LifecycleOutcomeEvents_Should_Constraint
{
    const int OutcomeEventId = 1307;

    static readonly ExecutionOptions DefaultOptions = new() { ReplicaTimeoutSeconds = 60 };

    static CanonicalMessage Message(string runtime = "Claude", string state = "status:ready-for-proposal") =>
        new(
            Vendor.GitHub,
            Project: null,
            Repo: "acme/widgets",
            ItemId: "42",
            State: state,
            Action: "/ds:propose",
            Runtime: runtime,
            OnFailureState: "status:blocked"
        );

    static (ExecutionService Service, RecordingLogger<ExecutionService> Logger) BuildService(
        FakeWorkItemQueueReader reader,
        FakeVendorAdapter adapter,
        IRuntimeRunner runner
    )
    {
        var logger = new RecordingLogger<ExecutionService>();
        var service = new ExecutionService(
            reader,
            [adapter],
            runner,
            Options.Create(DefaultOptions),
            logger
        );

        return (service, logger);
    }

    /// <summary>Asserts exactly one terminal event was emitted, with the expected outcome and a
    /// complete set of contract fields.</summary>
    static void ShouldHaveSingleOutcome(
        RecordingLogger<ExecutionService> logger,
        string expectedOutcome,
        CanonicalMessage message
    )
    {
        var outcomes = logger
            .Records.Where(record => record.Field("EventName") == LifecycleEvents.Outcome)
            .ToList();

        outcomes.ShouldHaveSingleItem();

        var outcome = outcomes[0];
        outcome.EventId.ShouldBe(OutcomeEventId);
        outcome.Field("Outcome").ShouldBe(expectedOutcome);
        outcome.Field("SchemaVersion").ShouldBe(LifecycleEvents.SchemaVersion.ToString());
        outcome.Field("Vendor").ShouldBe(message.Vendor.ToString());
        outcome.Field("Repo").ShouldBe(message.Repo);
        outcome.Field("ItemId").ShouldBe(message.ItemId);
        outcome.Field("Action").ShouldBe(message.Action);
        outcome.Field("Runtime").ShouldBe(message.Runtime);
        outcome.Field("LogPointer").ShouldBe(LifecycleOutcomes.LocalLogPointer);
        outcome.Fields.ShouldContainKey("DurationMs");
    }

    [Fact]
    public async Task RunOnceAsync_Should_EmitNoOutcomeWhenNothingWasDequeued()
    {
        var (service, logger) = BuildService(
            new FakeWorkItemQueueReader(),
            new FakeVendorAdapter(Vendor.GitHub, "status:ready-for-proposal"),
            new FakeRuntimeRunner()
        );

        await service.RunOnceAsync();

        // No message owned means no run happened -- an empty-queue wake-up must not show up in the
        // read model as a run at all.
        logger.Records.ShouldNotContain(record => record.Field("EventName") == LifecycleEvents.Outcome);
    }

    [Fact]
    public async Task RunOnceAsync_Should_EmitPoisonWhenTheDequeueLimitIsExceeded()
    {
        var message = Message();
        var (service, logger) = BuildService(
            FakeWorkItemQueueReader.WithMessage(message, dequeueCount: 99),
            new FakeVendorAdapter(Vendor.GitHub, message.State),
            new FakeRuntimeRunner()
        );

        await service.RunOnceAsync();

        ShouldHaveSingleOutcome(logger, LifecycleOutcomes.Poison, message);
    }

    [Fact]
    public async Task RunOnceAsync_Should_EmitPoisonWhenNoAdapterHandlesTheVendor()
    {
        var message = Message();
        var (service, logger) = BuildService(
            FakeWorkItemQueueReader.WithMessage(message),
            // The only registered adapter serves a different vendor than the message names.
            new FakeVendorAdapter(Vendor.GitLab, message.State),
            new FakeRuntimeRunner()
        );

        await service.RunOnceAsync();

        ShouldHaveSingleOutcome(logger, LifecycleOutcomes.Poison, message);
    }

    [Fact]
    public async Task RunOnceAsync_Should_EmitStaleGateWhenTheItemLeftTheGateState()
    {
        var message = Message();
        var (service, logger) = BuildService(
            FakeWorkItemQueueReader.WithMessage(message),
            new FakeVendorAdapter(Vendor.GitHub, "status:in-progress"),
            new FakeRuntimeRunner()
        );

        await service.RunOnceAsync();

        ShouldHaveSingleOutcome(logger, LifecycleOutcomes.StaleGate, message);
    }

    [Fact]
    public async Task RunOnceAsync_Should_EmitFailedWhenTheRuntimeIsUnknown()
    {
        var message = Message(runtime: "Cursor");
        var (service, logger) = BuildService(
            FakeWorkItemQueueReader.WithMessage(message),
            new FakeVendorAdapter(Vendor.GitHub, message.State),
            new FakeRuntimeRunner()
        );

        await service.RunOnceAsync();

        ShouldHaveSingleOutcome(logger, LifecycleOutcomes.Failed, message);
    }

    [Fact]
    public async Task RunOnceAsync_Should_EmitFailedWhenTheCloneFails()
    {
        var message = Message();
        var (service, logger) = BuildService(
            FakeWorkItemQueueReader.WithMessage(message),
            new FakeVendorAdapter(Vendor.GitHub, message.State),
            new FakeRuntimeRunner { NextResult = new RuntimeRunResult(1, false, "no such repo") }
        );

        await service.RunOnceAsync();

        ShouldHaveSingleOutcome(logger, LifecycleOutcomes.Failed, message);
    }

    [Fact]
    public async Task RunOnceAsync_Should_EmitSucceededWhenTheCommandExitsZero()
    {
        var message = Message();
        var (service, logger) = BuildService(
            FakeWorkItemQueueReader.WithMessage(message),
            new FakeVendorAdapter(Vendor.GitHub, message.State),
            new FakeRuntimeRunner { NextResult = new RuntimeRunResult(0, false, "ok") }
        );

        await service.RunOnceAsync();

        ShouldHaveSingleOutcome(logger, LifecycleOutcomes.Succeeded, message);
    }

    [Fact]
    public async Task RunOnceAsync_Should_EmitWipRefusedWhenTheCommandDeclinedOnTheWipLimit()
    {
        var message = Message();
        var cloneOk = new RuntimeRunResult(0, false, "ok");
        var (service, logger) = BuildService(
            FakeWorkItemQueueReader.WithMessage(message),
            new FakeVendorAdapter(Vendor.GitHub, message.State),
            new SequencedRuntimeRunner(
                [cloneOk, new RuntimeRunResult(1, false, "refusing: WIP limit reached")]
            )
        );

        await service.RunOnceAsync();

        // A WIP refusal is neither success nor failure: the command ran and declined, and the gate
        // state was kept. It gets its own outcome so the run is still visible in history rather
        // than being miscounted as a failure.
        ShouldHaveSingleOutcome(logger, LifecycleOutcomes.WipRefused, message);
    }

    [Fact]
    public async Task RunOnceAsync_Should_EmitFailedWhenTheCommandExitsNonZero()
    {
        var message = Message();
        var cloneOk = new RuntimeRunResult(0, false, "ok");
        var (service, logger) = BuildService(
            FakeWorkItemQueueReader.WithMessage(message),
            new FakeVendorAdapter(Vendor.GitHub, message.State),
            new SequencedRuntimeRunner([cloneOk, new RuntimeRunResult(1, false, "boom")])
        );

        await service.RunOnceAsync();

        ShouldHaveSingleOutcome(logger, LifecycleOutcomes.Failed, message);
    }

    sealed class SequencedRuntimeRunner(Queue<RuntimeRunResult> results) : IRuntimeRunner
    {
        public SequencedRuntimeRunner(IEnumerable<RuntimeRunResult> results)
            : this(new Queue<RuntimeRunResult>(results)) { }

        public Task<RuntimeRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string?> environment,
            TimeSpan timeout,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(results.Dequeue());
    }
}
