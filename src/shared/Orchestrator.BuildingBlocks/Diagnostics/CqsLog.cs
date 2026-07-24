using Microsoft.Extensions.Logging;

namespace Orchestrator.BuildingBlocks.Diagnostics;

/// <summary>
/// Source-generated CQS pipeline logging. EventId range: 1000-1099.
/// </summary>
public static partial class CqsLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Debug, Message = "Handling {RequestName}")]
    public static partial void HandlingRequest(ILogger logger, string requestName);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Debug,
        Message = "Handled {RequestName} successfully"
    )]
    public static partial void RequestSucceeded(ILogger logger, string requestName);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Unhandled exception while handling {RequestName}"
    )]
    public static partial void RequestFailed(
        ILogger logger,
        Exception exception,
        string requestName
    );

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Warning,
        Message = "{RequestName} returned a functional error: {Errors}"
    )]
    public static partial void RequestReturnedFunctionalError(
        ILogger logger,
        string requestName,
        string errors
    );
}
