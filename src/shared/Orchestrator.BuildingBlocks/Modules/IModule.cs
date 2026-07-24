using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Orchestrator.BuildingBlocks.Modules;

/// <summary>
/// Self-registering module lifecycle: <c>Add</c> → <c>InitializeAsync</c>, in that order. Unlike a
/// single-host modular monolith, this product composes two hosts (Receiver, Agent) that load
/// different subsets of modules, so <see cref="IModule"/> itself carries no HTTP-specific
/// contract — modules that expose endpoints additionally implement <see cref="IEndpointModule"/>.
/// </summary>
public interface IModule
{
    void Add(IServiceCollection services, IConfiguration configuration);

    Task InitializeAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default
    );
}
