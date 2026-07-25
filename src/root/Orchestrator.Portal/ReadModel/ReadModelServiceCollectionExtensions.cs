using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Orchestrator.Portal.ReadModel;

public static class ReadModelServiceCollectionExtensions
{
    /// <summary>
    /// Registers the read-model sources and the caching/degrading reader in front of them.
    /// <para>
    /// One <see cref="DefaultAzureCredential"/> is shared across sources: it caches tokens internally,
    /// so constructing it per source would multiply token acquisitions for no benefit. Every Azure read
    /// the portal does goes through the portal's own managed identity — it never holds a stack's
    /// write-capable secrets (design D7).
    /// </para>
    /// </summary>
    public static IServiceCollection AddReadModel(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.Configure<ReadModelOptions>(
            configuration.GetSection(ReadModelOptions.SectionName)
        );

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<TokenCredential>(new DefaultAzureCredential());
        services.AddSingleton<IPanelReader, CachedPanelReader>();

        services.AddSingleton<QueueDepthSource>();

        return services;
    }
}
