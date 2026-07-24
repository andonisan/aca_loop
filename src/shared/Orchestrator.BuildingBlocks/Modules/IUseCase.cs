using Microsoft.AspNetCore.Routing;

namespace Orchestrator.BuildingBlocks.Modules;

/// <summary>A minimal-API endpoint group, discovered by reflection and mapped by <see cref="ModuleBase"/>.</summary>
public interface IUseCase
{
    static abstract void AddRoutes(IEndpointRouteBuilder endpoints);
}
