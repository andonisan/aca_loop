using Orchestrator.BuildingBlocks.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Orchestrator.Modules.Dispatch;

/// <summary>Composed only by the Receiver host — owns the vendor-agnostic webhook endpoint and the
/// enqueue side of the AI-pod dispatch pipeline.</summary>
public sealed class DispatchModule : EndpointModuleBase
{
    public override void Add(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<QueueOptions>(configuration.GetSection(QueueOptions.SectionName));

        services.AddSingleton(sp => QueueClientFactory.Create(sp.GetRequiredService<IOptions<QueueOptions>>()));
        services.AddSingleton<IWorkItemQueue, AzureStorageWorkItemQueue>();

        // Loaded and validated eagerly, here, before the host finishes building — an invalid
        // mapping throws and the process never reaches app.Run(), satisfying "fail fast, no
        // traffic served" (state-action-config spec).
        var mappingOptions = new MappingOptions();
        configuration.GetSection(MappingOptions.SectionName).Bind(mappingOptions);
        var mappingConfig = MappingLoader.LoadFromFile(mappingOptions.FilePath);
        services.AddSingleton<IMappingProvider>(new MappingProvider(mappingConfig));
    }
}
