using Microsoft.Extensions.Logging;

namespace Orchestrator.Modules.Execution;

/// <summary>Source-generated Execution logging. EventId range: 1300-1399.</summary>
static partial class ExecutionLog
{
    [LoggerMessage(
        EventId = 1300,
        Level = LogLevel.Information,
        Message = "Queue empty, nothing to do"
    )]
    public static partial void QueueEmpty(ILogger logger);

    [LoggerMessage(
        EventId = 1301,
        Level = LogLevel.Warning,
        Message = "Poison message (dequeue_count={DequeueCount}), dropping {Vendor} {Repo}#{ItemId}"
    )]
    public static partial void PoisonMessage(
        ILogger logger,
        long dequeueCount,
        string vendor,
        string repo,
        string itemId
    );

    [LoggerMessage(
        EventId = 1302,
        Level = LogLevel.Information,
        Message = "Picked up {Repo}#{ItemId} at state {State} (runtime={Runtime}, attempt {Attempt})"
    )]
    public static partial void PickedUp(
        ILogger logger,
        string repo,
        string itemId,
        string state,
        string runtime,
        long attempt
    );

    [LoggerMessage(
        EventId = 1303,
        Level = LogLevel.Information,
        Message = "State {State} no longer on {Repo}#{ItemId}, exiting clean"
    )]
    public static partial void StaleGate(ILogger logger, string state, string repo, string itemId);

    [LoggerMessage(
        EventId = 1304,
        Level = LogLevel.Information,
        Message = "{Repo}#{ItemId} completed"
    )]
    public static partial void Completed(ILogger logger, string repo, string itemId);

    [LoggerMessage(
        EventId = 1305,
        Level = LogLevel.Warning,
        Message = "{Repo}#{ItemId} refused by WIP limit, keeping state"
    )]
    public static partial void WipRefused(ILogger logger, string repo, string itemId);

    [LoggerMessage(
        EventId = 1306,
        Level = LogLevel.Error,
        Message = "{Repo}#{ItemId} failed: {Reason}"
    )]
    public static partial void RunFailed(ILogger logger, string repo, string itemId, string reason);

    /// <summary>Additional context on an already-recorded failure, never a cause of its own. On a
    /// workflow-constrained tracker the failure state can be genuinely unreachable from the item's
    /// current status (jira-adapter design.md D5), so this is an expected outcome rather than a bug.</summary>
    [LoggerMessage(
        EventId = 1308,
        Level = LogLevel.Warning,
        Message = "Could not move {Repo}#{ItemId} to {OnFailureState}, leaving state unchanged: {Reason}"
    )]
    public static partial void FailureTransitionUnavailable(
        ILogger logger,
        string repo,
        string itemId,
        string onFailureState,
        string reason
    );

    /// <summary>
    /// The <c>dispatch.outcome</c> half of the normalized lifecycle contract
    /// (<see cref="Orchestrator.Constants.LifecycleEvents"/>) — emitted exactly once by every
    /// execution that dequeued a message, on every terminal path. The events above stay as the
    /// human-readable narrative; this one is the machine contract the Portal queries, which is why
    /// it repeats their coordinates instead of being derived from them at query time.
    /// </summary>
    [LoggerMessage(
        EventId = 1307,
        Level = LogLevel.Information,
        Message = "{EventName} v{SchemaVersion}: {Vendor} {Project} {Repo}#{ItemId} {Action} ({Runtime}) → {Outcome} in {DurationMs}ms [{LogPointer}]"
    )]
    public static partial void Outcome(
        ILogger logger,
        string eventName,
        int schemaVersion,
        string vendor,
        string? project,
        string repo,
        string itemId,
        string action,
        string runtime,
        string outcome,
        long durationMs,
        string logPointer
    );
}
