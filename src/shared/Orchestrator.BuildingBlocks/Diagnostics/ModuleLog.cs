using Microsoft.Extensions.Logging;

namespace Orchestrator.BuildingBlocks.Diagnostics;

/// <summary>
/// Source-generated module registration/endpoint logging. EventId range: 1200-1299.
/// </summary>
public static partial class ModuleLog
{
    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Information,
        Message = "Registered module {ModuleName}"
    )]
    public static partial void ModuleRegistered(ILogger logger, string moduleName);

    [LoggerMessage(
        EventId = 1201,
        Level = LogLevel.Debug,
        Message = "Mapped endpoints for module {ModuleName}"
    )]
    public static partial void ModuleEndpointsMapped(ILogger logger, string moduleName);

    [LoggerMessage(
        EventId = 1202,
        Level = LogLevel.Information,
        Message = "Initialized module {ModuleName}"
    )]
    public static partial void ModuleInitialized(ILogger logger, string moduleName);
}
