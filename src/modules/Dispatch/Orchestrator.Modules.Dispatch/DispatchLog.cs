using Microsoft.Extensions.Logging;

namespace Orchestrator.Modules.Dispatch;

/// <summary>Source-generated Dispatch logging. EventId range: 1400-1499.</summary>
static partial class DispatchLog
{
    /// <summary>
    /// The <c>dispatch.enqueued</c> half of the normalized lifecycle contract
    /// (<see cref="Orchestrator.Constants.LifecycleEvents"/>) — emitted once per accepted webhook,
    /// after the message is on the queue, so a row here means the work was really handed off.
    /// </summary>
    [LoggerMessage(
        EventId = 1400,
        Level = LogLevel.Information,
        Message = "{EventName} v{SchemaVersion}: {Vendor} {Project} {Repo}#{ItemId} at {State} → {Action} ({Runtime})"
    )]
    public static partial void Enqueued(
        ILogger logger,
        string eventName,
        int schemaVersion,
        string vendor,
        string? project,
        string repo,
        string itemId,
        string state,
        string action,
        string runtime
    );
}
