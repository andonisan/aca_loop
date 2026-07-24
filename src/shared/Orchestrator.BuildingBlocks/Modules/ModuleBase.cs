using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Orchestrator.BuildingBlocks.Modules;

public abstract class ModuleBase : IModule
{
    public virtual void Add(IServiceCollection services, IConfiguration configuration) { }

    public virtual Task InitializeAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    /// <summary>Discovers every <see cref="IUseCase"/> in this module's assembly and maps its routes.</summary>
    protected void MapUseCaseEndpoints(IEndpointRouteBuilder endpoints)
    {
        var useCaseTypes = GetType()
            .Assembly.GetTypes()
            .Where(t =>
                t is { IsAbstract: false, IsInterface: false }
                && typeof(IUseCase).IsAssignableFrom(t)
            );

        foreach (var useCaseType in useCaseTypes)
        {
            var addRoutes =
                useCaseType.GetMethod(
                    nameof(IUseCase.AddRoutes),
                    BindingFlags.Public | BindingFlags.Static
                )
                ?? throw new InvalidOperationException(
                    $"{useCaseType.Name} does not implement a public static AddRoutes method."
                );

            addRoutes.Invoke(null, [endpoints]);
        }
    }
}

/// <summary>Base for modules composed only by the Agent host (no HTTP endpoints).</summary>
public abstract class WorkerModuleBase : ModuleBase;

/// <summary>Base for modules composed by the Receiver host, exposing minimal-API endpoints.</summary>
public abstract class EndpointModuleBase : ModuleBase, IEndpointModule
{
    public virtual void Use(WebApplication app) => MapUseCaseEndpoints(app);
}
