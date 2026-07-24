using Microsoft.AspNetCore.Builder;

namespace Orchestrator.BuildingBlocks.Modules;

/// <summary>
/// A module that maps HTTP endpoints. Only composed by <c>Orchestrator.Receiver</c> — the Agent
/// host never calls <see cref="Use"/>, since it is a worker process with no HTTP surface.
/// </summary>
public interface IEndpointModule : IModule
{
    void Use(WebApplication app);
}
