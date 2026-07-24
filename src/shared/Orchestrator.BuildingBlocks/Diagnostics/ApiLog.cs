using Microsoft.Extensions.Logging;

namespace Orchestrator.BuildingBlocks.Diagnostics;

/// <summary>
/// Source-generated API-edge logging. EventId range: 1100-1199.
/// </summary>
public static partial class ApiLog
{
    [LoggerMessage(EventId = 1100, Level = LogLevel.Error, Message = "Unhandled exception")]
    public static partial void UnhandledException(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning, Message = "Malformed request body")]
    public static partial void MalformedRequestBody(ILogger logger, Exception exception);
}
