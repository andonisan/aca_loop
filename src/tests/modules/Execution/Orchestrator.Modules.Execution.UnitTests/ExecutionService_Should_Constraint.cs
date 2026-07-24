using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orchestrator.Modules.Backlog.Contracts;
using Shouldly;

namespace Orchestrator.Modules.Execution.UnitTests;

public class ExecutionService_Should_Constraint
{
    static readonly ExecutionOptions DefaultOptions = new() { ReplicaTimeoutSeconds = 60 };

    static CanonicalMessage Message(
        string runtime = "Claude",
        string state = "status:ready-for-proposal"
    ) =>
        new(
            Vendor.GitHub,
            Project: null,
            Repo: "acme/widgets",
            ItemId: "42",
            State: state,
            Action: "/ds:propose",
            Runtime: runtime,
            OnFailureState: "status:blocked",
            CodeHost: Vendor.GitHub,
            CodeRepo: "acme/widgets"
        );

    /// <summary>The fake serves as both tracker and code host, matching every currently-shipping
    /// vendor. Tests that need them to differ pass the code-host list explicitly.</summary>
    static ExecutionService BuildService(
        FakeWorkItemQueueReader reader,
        FakeVendorAdapter adapter,
        IRuntimeRunner runner,
        ExecutionOptions? options = null,
        IEnumerable<ICodeHostAdapter>? codeHosts = null
    ) =>
        new(
            reader,
            [adapter],
            codeHosts ?? [adapter],
            runner,
            Options.Create(options ?? DefaultOptions),
            NullLogger<ExecutionService>.Instance
        );

    [Fact]
    public async Task RunOnceAsync_Should_ExitCleanWhenQueueIsEmpty()
    {
        var reader = new FakeWorkItemQueueReader();
        var adapter = new FakeVendorAdapter(Vendor.GitHub, "status:ready-for-proposal");
        var runner = new FakeRuntimeRunner();

        await BuildService(reader, adapter, runner).RunOnceAsync();

        reader.Deleted.ShouldBeEmpty();
        runner.InvokedFileNames.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_Should_ExitCleanWhenGateIsStale()
    {
        var message = Message(state: "status:ready-for-proposal");
        var reader = FakeWorkItemQueueReader.WithMessage(message);
        // Live state no longer matches the message's state -- another execution/human already moved it.
        var adapter = new FakeVendorAdapter(Vendor.GitHub, "status:in-progress");
        var runner = new FakeRuntimeRunner();

        await BuildService(reader, adapter, runner).RunOnceAsync();

        reader.Deleted.ShouldHaveSingleItem();
        adapter.Transitions.ShouldBeEmpty();
        runner.InvokedFileNames.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_Should_DropAPoisonMessageWithoutRunningAnything()
    {
        var message = Message();
        var reader = FakeWorkItemQueueReader.WithMessage(message, dequeueCount: 99);
        var adapter = new FakeVendorAdapter(Vendor.GitHub, message.State);
        var runner = new FakeRuntimeRunner();

        await BuildService(reader, adapter, runner).RunOnceAsync();

        reader.Deleted.ShouldHaveSingleItem();
        runner.InvokedFileNames.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_Should_FailWithoutRunningAnyCommandWhenRuntimeIsUnknown()
    {
        var message = Message(runtime: "Cursor");
        var reader = FakeWorkItemQueueReader.WithMessage(message);
        var adapter = new FakeVendorAdapter(Vendor.GitHub, message.State);
        var runner = new FakeRuntimeRunner();

        await BuildService(reader, adapter, runner).RunOnceAsync();

        // No command executed at all -- not even the git clone, since the runtime is validated
        // before any credential is minted (agent-runtime-selection spec).
        runner.InvokedFileNames.ShouldBeEmpty();
        adapter.Transitions.ShouldHaveSingleItem();
        adapter.Transitions[0].NewState.ShouldBe(message.OnFailureState);
        adapter.Comments.ShouldHaveSingleItem();
        adapter.Comments[0].ShouldContain("unknown runtime");
        reader.Deleted.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task RunOnceAsync_Should_FailWithoutRunningAnyCommandWhenCodeHostIsUnregistered()
    {
        // Tracker resolves, code host does not -- a mapping or version-skew bug. It takes the same
        // failure contract as an unknown runtime rather than poisoning silently, because the tracker
        // adapter is available to write the reason onto the item.
        var message = Message() with
        {
            CodeHost = Vendor.GitLab,
            CodeRepo = "acme/widgets",
        };
        var reader = FakeWorkItemQueueReader.WithMessage(message);
        var adapter = new FakeVendorAdapter(Vendor.GitHub, message.State);
        var runner = new FakeRuntimeRunner();

        await BuildService(reader, adapter, runner, codeHosts: []).RunOnceAsync();

        runner.InvokedFileNames.ShouldBeEmpty();
        adapter.Transitions.ShouldHaveSingleItem();
        adapter.Transitions[0].NewState.ShouldBe(message.OnFailureState);
        adapter.Comments.ShouldHaveSingleItem();
        adapter.Comments[0].ShouldContain("unknown code host");
        reader.Deleted.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task RunOnceAsync_Should_TakeCliEnvironmentFromTheCodeHostNotTheTracker()
    {
        // The tracker is GitHub, the code host is a separate fake. The run must carry the code
        // host's CLI token, which is what moving this off Execution's vendor switch buys: the switch
        // keyed on the tracker and had no Azure DevOps case at all.
        var message = Message() with
        {
            CodeHost = Vendor.AzureDevOps,
            CodeRepo = "acme/widgets",
        };
        var reader = FakeWorkItemQueueReader.WithMessage(message);
        var tracker = new FakeVendorAdapter(Vendor.GitHub, message.State);
        var codeHost = new FakeVendorAdapter(Vendor.AzureDevOps, message.State);
        var runner = new FakeRuntimeRunner { NextResult = new RuntimeRunResult(0, false, "ok") };

        await BuildService(reader, tracker, runner, codeHosts: [codeHost]).RunOnceAsync();

        runner.InvokedFileNames.ShouldBe(["git", "claude"]);
        runner.LastEnvironment.ShouldContainKey("FAKE_TOKEN");
        // Gate re-validation and write-back stayed on the tracker.
        tracker.Transitions.ShouldBeEmpty();
        reader.Deleted.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task RunOnceAsync_Should_DeleteTheMessageOnASuccessfulRunWithoutTransitioningState()
    {
        var message = Message();
        var reader = FakeWorkItemQueueReader.WithMessage(message);
        var adapter = new FakeVendorAdapter(Vendor.GitHub, message.State);
        var runner = new FakeRuntimeRunner { NextResult = new RuntimeRunResult(0, false, "ok") };

        await BuildService(reader, adapter, runner).RunOnceAsync();

        // git clone, then the runtime CLI -- both via the same fake runner.
        runner.InvokedFileNames.ShouldBe(["git", "claude"]);
        adapter.Transitions.ShouldBeEmpty();
        reader.Deleted.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task RunOnceAsync_Should_InvokeOpenCodeWhenMessageRuntimeIsOpenCode()
    {
        var message = Message(runtime: "OpenCode");
        var reader = FakeWorkItemQueueReader.WithMessage(message);
        var adapter = new FakeVendorAdapter(Vendor.GitHub, message.State);
        var runner = new FakeRuntimeRunner { NextResult = new RuntimeRunResult(0, false, "ok") };

        await BuildService(reader, adapter, runner).RunOnceAsync();

        // Same code path as the Claude case above -- only the resolved binary name differs; this is
        // the mechanical half of the agent-runtime-selection spec's "same command, either runtime"
        // requirement. The other half -- a real target repo's .claude/commands and .opencode/commands
        // both actually invoking the same command -- needs a live repo and real credentials, which
        // this sandboxed session doesn't have; see tasks.md 3.2.
        runner.InvokedFileNames.ShouldBe(["git", "opencode"]);
        reader.Deleted.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task RunOnceAsync_Should_TransitionToOnFailureStateWhenTheRuntimeFails()
    {
        var message = Message();
        var reader = FakeWorkItemQueueReader.WithMessage(message);
        var adapter = new FakeVendorAdapter(Vendor.GitHub, message.State);
        var cloneOk = new RuntimeRunResult(0, false, "ok");
        var runner = new SequencedRuntimeRunner([cloneOk, new RuntimeRunResult(1, false, "boom")]);

        await BuildService(reader, adapter, runner).RunOnceAsync();

        adapter.Transitions.ShouldHaveSingleItem();
        adapter.Transitions[0].NewState.ShouldBe(message.OnFailureState);
        reader.Deleted.ShouldHaveSingleItem();
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
