using System.Reflection;
using Orchestrator.BuildingBlocks.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Orchestrator.BuildingBlocks.Modules;

public static class ModuleExtensions
{
    const string ModuleAssemblyPrefix = "Orchestrator.Modules.";
    const string ContractsAssemblySuffix = ".Contracts.dll";

    public static IServiceCollection RegisterAppModules(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        foreach (var module in DiscoverModules())
        {
            services.AddSingleton(module);
            module.Add(services, configuration);
        }

        return services;
    }

    /// <summary>Maps HTTP endpoints for every discovered module that implements <see cref="IEndpointModule"/>.
    /// Only the Receiver host calls this — the Agent host has no HTTP surface.</summary>
    public static WebApplication UseAppModules(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILogger<IModule>>();

        foreach (var module in app.Services.GetServices<IModule>().OfType<IEndpointModule>())
        {
            module.Use(app);
            ModuleLog.ModuleRegistered(logger, module.GetType().Name);
        }

        return app;
    }

    public static async Task<IServiceProvider> InitializeAppModulesAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default
    )
    {
        var logger = serviceProvider.GetRequiredService<ILogger<IModule>>();

        using var scope = serviceProvider.CreateScope();
        foreach (var module in serviceProvider.GetServices<IModule>())
        {
            await module.InitializeAsync(scope.ServiceProvider, cancellationToken);
            ModuleLog.ModuleInitialized(logger, module.GetType().Name);
        }

        return serviceProvider;
    }

    /// <summary>
    /// Loads every <c>Orchestrator.Modules.*.dll</c> (excluding <c>.Contracts</c>) from the host's output
    /// directory and instantiates its <see cref="IModule"/> implementations, so adding a module needs
    /// no edit to the host's composition code.
    /// </summary>
    static IReadOnlyCollection<IModule> DiscoverModules()
    {
        return
        [
            .. Directory
                .GetFiles(AppContext.BaseDirectory, $"{ModuleAssemblyPrefix}*.dll")
                .Where(path => !path.EndsWith(ContractsAssemblySuffix, StringComparison.Ordinal))
                .Select(Assembly.LoadFrom)
                .SelectMany(a => a.GetTypes())
                .Where(t =>
                    t is { IsAbstract: false, IsInterface: false }
                    && typeof(IModule).IsAssignableFrom(t)
                )
                .Select(t => (IModule)Activator.CreateInstance(t)!),
        ];
    }
}
