using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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

        services.AddSingleton(sp =>
            StackQueueClient.ForCredential(sp.GetRequiredService<TokenCredential>())
        );
        services.AddHttpClient("vendor-read");
        services.AddSingleton<IReadCredentialResolver, ReadCredentialResolver>();
        services.AddSingleton<BacklogReaderFactory>();
        services.AddSingleton<GatedItemsSource>();
        services.AddSingleton<Operations.IManualDispatch, Operations.ManualDispatch>();
        services.AddSingleton<Operations.ICancelExecution, Operations.CancelExecution>();

        services.AddSingleton<QueueDepthSource>();
        services.AddSingleton<PodExecutionsSource>();

        // Constructed per window rather than registered as a singleton: the window is part of the
        // source's cache identity, so a 7-day view never serves rows to a 24-hour one.
        services.AddSingleton<Func<TimeSpan, RunHistorySource>>(sp =>
            window => new RunHistorySource(
                sp.GetRequiredService<TokenCredential>(),
                sp.GetRequiredService<IOptions<ReadModelOptions>>(),
                window
            )
        );

        return services;
    }
}
