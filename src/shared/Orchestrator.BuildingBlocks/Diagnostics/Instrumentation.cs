using System.Diagnostics;
using Orchestrator.Constants;

namespace Orchestrator.BuildingBlocks.Diagnostics;

/// <summary>The single <see cref="ActivitySource"/> shared by every module and host.</summary>
public static class Instrumentation
{
    public static readonly ActivitySource Source = new(LoggingNames.InstrumentationSourceName);
}
