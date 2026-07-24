using System.Diagnostics;
using System.Text.RegularExpressions;
using Orchestrator.Constants;
using Orchestrator.Modules.Backlog.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Orchestrator.Modules.Execution;

/// <summary>
/// The Agent's per-execution entrypoint contract — ported from v1's <c>agent/src/entrypoint.js</c>:
/// dequeue → poison-message guard → gate re-validation → mint credentials → clone → run the mapped
/// action headlessly → on failure, transition to the message's <c>OnFailureState</c> (resolved by
/// Dispatch from the state-action-config mapping) with a log-tail comment. One call to
/// <see cref="RunOnceAsync"/> handles exactly one queue message (or none).
/// </summary>
public sealed class ExecutionService(
    IWorkItemQueueReader queueReader,
    IEnumerable<IVendorAdapter> adapters,
    IRuntimeRunner runtimeRunner,
    IOptions<ExecutionOptions> options,
    ILogger<ExecutionService> logger
)
{
    /// <summary>The ACA-injected execution name, or a local marker off-platform — see
    /// <see cref="LifecycleOutcomes.LogPointerEnvironmentVariable"/>. Read once: it is fixed for
    /// the lifetime of a job execution.</summary>
    static readonly string LogPointer =
        Environment.GetEnvironmentVariable(LifecycleOutcomes.LogPointerEnvironmentVariable)
        is { Length: > 0 } pointer
            ? pointer
            : LifecycleOutcomes.LocalLogPointer;

    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var executionOptions = options.Value;

        var received = await queueReader.ReceiveAsync(
            TimeSpan.FromSeconds(executionOptions.ReplicaTimeoutSeconds),
            cancellationToken
        );
        if (received is null)
        {
            ExecutionLog.QueueEmpty(logger);
            return;
        }

        var message = received.Payload;

        // Timed from the moment this execution owns a message, so a duration on the outcome event
        // covers everything this run was responsible for -- including the paths that never get as
        // far as running a command.
        var elapsed = Stopwatch.StartNew();

        if (received.DequeueCount > executionOptions.MaxDequeueCount)
        {
            ExecutionLog.PoisonMessage(
                logger,
                received.DequeueCount,
                message.Vendor.ToString(),
                message.Repo,
                message.ItemId
            );
            EmitOutcome(message, LifecycleOutcomes.Poison, elapsed);
            await queueReader.DeleteAsync(received, cancellationToken);
            return;
        }

        var adapter = adapters.FirstOrDefault(a => a.Vendor == message.Vendor);
        if (adapter is null)
        {
            ExecutionLog.PoisonMessage(logger, received.DequeueCount, message.Vendor.ToString(), message.Repo, message.ItemId);
            EmitOutcome(message, LifecycleOutcomes.Poison, elapsed);
            await queueReader.DeleteAsync(received, cancellationToken);
            return;
        }

        ExecutionLog.PickedUp(
            logger,
            message.Repo,
            message.ItemId,
            message.State,
            message.Runtime,
            received.DequeueCount
        );

        // Gate re-validation: the single-state invariant means the state moving is the lock. A
        // duplicate delivery, a human re-triaging, or another execution having already started all
        // look the same -- the gate state is gone, so this execution has nothing to do.
        var currentState = await adapter.ReadItemStateAsync(
            message.Project,
            message.Repo,
            message.ItemId,
            cancellationToken
        );
        if (currentState != message.State)
        {
            ExecutionLog.StaleGate(logger, message.State, message.Repo, message.ItemId);
            EmitOutcome(message, LifecycleOutcomes.StaleGate, elapsed);
            await queueReader.DeleteAsync(received, cancellationToken);
            return;
        }

        // An unparseable runtime fails the run through the normal failure contract, before any
        // credential is minted or repo cloned -- no command is ever executed (agent-runtime-
        // selection spec). Dispatch always writes a value it already validated against the
        // mapping; this only guards against message tampering or a version-skew bug.
        if (!Enum.TryParse<AgentRuntime>(message.Runtime, ignoreCase: true, out var runtime))
        {
            await FailAsync(adapter, message, received, $"unknown runtime '{message.Runtime}'", "", elapsed, cancellationToken);
            return;
        }

        var credentials = await adapter.GetCloneCredentialsAsync(
            message.Project,
            message.Repo,
            cancellationToken
        );

        var workDir = Directory.CreateTempSubdirectory("ai-loop-");
        var repoDir = Path.Combine(workDir.FullName, "repo");

        var environment = new Dictionary<string, string?>
        {
            ["GIT_AUTHOR_NAME"] = credentials.GitAuthorName,
            ["GIT_AUTHOR_EMAIL"] = credentials.GitAuthorEmail,
            ["GIT_COMMITTER_NAME"] = credentials.GitAuthorName,
            ["GIT_COMMITTER_EMAIL"] = credentials.GitAuthorEmail,
        };
        // Vendor CLI auth env vars, not just git's -- so `gh` never sees a GitLab token or vice
        // versa (the target repo's own command drives whichever CLI its vendor needs).
        switch (message.Vendor)
        {
            case Vendor.GitHub:
                environment["GH_TOKEN"] = credentials.Token;
                environment["GITHUB_TOKEN"] = credentials.Token;
                break;
            case Vendor.GitLab:
                environment["GITLAB_TOKEN"] = credentials.Token;
                break;
        }

        var cloneResult = await runtimeRunner.RunAsync(
            "git",
            ["clone", credentials.CloneUrl, repoDir],
            workDir.FullName,
            environment,
            TimeSpan.FromMinutes(5),
            cancellationToken
        );
        if (!cloneResult.Succeeded)
        {
            await FailAsync(
                adapter,
                message,
                received,
                $"failed to clone: exited with code {cloneResult.ExitCode}",
                cloneResult.Output,
                elapsed,
                cancellationToken
            );
            return;
        }

        var prompt = $"{message.Action} #{message.ItemId}";
        var timeoutSeconds = Math.Max(
            60,
            executionOptions.ReplicaTimeoutSeconds - executionOptions.WatchdogBufferSeconds
        );

        var (fileName, arguments) = BuildInvocation(runtime, prompt, executionOptions);

        var result = await runtimeRunner.RunAsync(
            fileName,
            arguments,
            repoDir,
            environment,
            TimeSpan.FromSeconds(timeoutSeconds),
            cancellationToken
        );

        if (result.Succeeded)
        {
            // Branching, PR creation, and the state transition on success all belong to the repo's
            // own command -- success here just means the run completed.
            await queueReader.DeleteAsync(received, cancellationToken);
            ExecutionLog.Completed(logger, message.Repo, message.ItemId);
            EmitOutcome(message, LifecycleOutcomes.Succeeded, elapsed);
            return;
        }

        if (Regex.IsMatch(result.Output, executionOptions.WipRefusalPattern, RegexOptions.IgnoreCase))
        {
            ExecutionLog.WipRefused(logger, message.Repo, message.ItemId);
            await adapter.PostCommentAsync(
                message.Project,
                message.Repo,
                message.ItemId,
                $"🚦 The agent declined `{message.Action}`: the WIP limit is reached. The `{message.State}` "
                    + "state was kept — retry once an in-progress item completes.",
                cancellationToken
            );
            EmitOutcome(message, LifecycleOutcomes.WipRefused, elapsed);
            await queueReader.DeleteAsync(received, cancellationToken);
            return;
        }

        var reason = result.TimedOut
            ? $"timed out after {timeoutSeconds}s"
            : $"exited with code {result.ExitCode}";
        await FailAsync(adapter, message, received, reason, result.Output, elapsed, cancellationToken);
    }

    async Task FailAsync(
        IVendorAdapter adapter,
        CanonicalMessage message,
        ReceivedWorkItem received,
        string reason,
        string output,
        Stopwatch elapsed,
        CancellationToken cancellationToken
    )
    {
        ExecutionLog.RunFailed(logger, message.Repo, message.ItemId, reason);
        EmitOutcome(message, LifecycleOutcomes.Failed, elapsed);

        try
        {
            await adapter.TransitionStateAsync(
                message.Project,
                message.Repo,
                message.ItemId,
                message.OnFailureState,
                cancellationToken
            );
            await adapter.PostCommentAsync(
                message.Project,
                message.Repo,
                message.ItemId,
                BuildFailureComment(message, reason, output),
                cancellationToken
            );
        }
        finally
        {
            await queueReader.DeleteAsync(received, cancellationToken);
        }
    }

    /// <summary>Emits the one normalized terminal event for this execution (portal-read-model spec).
    /// Every early return in <see cref="RunOnceAsync"/> passes through here exactly once.</summary>
    void EmitOutcome(CanonicalMessage message, string outcome, Stopwatch elapsed) =>
        ExecutionLog.Outcome(
            logger,
            LifecycleEvents.Outcome,
            LifecycleEvents.SchemaVersion,
            message.Vendor.ToString(),
            message.Project,
            message.Repo,
            message.ItemId,
            message.Action,
            message.Runtime,
            outcome,
            elapsed.ElapsedMilliseconds,
            LogPointer
        );

    static string BuildFailureComment(CanonicalMessage message, string reason, string output)
    {
        const int maxChars = 6000;
        var tail = output.Length <= maxChars ? output : $"…\n{output[^maxChars..]}";

        return string.Join(
            '\n',
            $"❌ Agent run for `{message.Action}` (runtime: `{message.Runtime}`) {reason}.",
            "",
            $"The item was moved to `{message.OnFailureState}`. Any partial branch or draft PR is "
                + "left in place for inspection.",
            "",
            "<details><summary>Log tail</summary>",
            "",
            "```",
            tail,
            "```",
            "",
            "</details>"
        );
    }

    static (string FileName, IReadOnlyList<string> Arguments) BuildInvocation(
        AgentRuntime runtime,
        string prompt,
        ExecutionOptions options
    ) =>
        runtime switch
        {
            AgentRuntime.OpenCode => ("opencode", ["run", prompt]),
            _ => (
                "claude",
                [
                    "-p",
                    prompt,
                    "--max-turns",
                    options.MaxTurns.ToString(),
                    "--allowed-tools",
                    options.AllowedTools,
                ]
            ),
        };
}
