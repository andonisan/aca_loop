namespace Orchestrator.Portal;

/// <summary>Source-generated Portal logging. EventId range: 1500-1599.</summary>
static partial class PortalLog
{
    /// <summary>Logged once per failed read rather than per render: the panel is cached, including its
    /// failure, so a flapping source produces one line per TTL instead of one per poll.</summary>
    [LoggerMessage(
        EventId = 1500,
        Level = LogLevel.Warning,
        Message = "Read-model source {Source} unavailable for stack {StackId}: {Reason}"
    )]
    public static partial void PanelSourceFailed(
        ILogger logger,
        string source,
        string stackId,
        string reason
    );
}
