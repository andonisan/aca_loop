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

    /// <summary>
    /// The audit record for an operational verb. Every verb path ends in exactly one of these, with an
    /// outcome from a closed set — <c>performed</c>, <c>refused</c>, <c>failed</c> — because an audit trail
    /// that cannot distinguish "we declined" from "we broke" answers neither question.
    /// </summary>
    [LoggerMessage(
        EventId = 1501,
        Level = LogLevel.Information,
        Message = "portal.verb {Verb} by {Actor} on {StackId}/{Target} → {Outcome} ({Detail})"
    )]
    public static partial void VerbPerformed(
        ILogger logger,
        string verb,
        string actor,
        string stackId,
        string target,
        string outcome,
        string? detail
    );
}
