using Orchestrator.BuildingBlocks.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Orchestrator.Modules.Execution;

/// <summary>Composed only by the Agent host — the dequeue side of the AI-pod dispatch pipeline and
/// the per-run entrypoint contract (gate re-validation, runtime execution, failure write-back).</summary>
public sealed class ExecutionModule : WorkerModuleBase
{
    public override void Add(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<QueueOptions>(configuration.GetSection(QueueOptions.SectionName));
        services.Configure<ExecutionOptions>(configuration.GetSection(ExecutionOptions.SectionName));

        services.AddSingleton(sp => QueueClientFactory.Create(sp.GetRequiredService<IOptions<QueueOptions>>()));
        services.AddSingleton<IWorkItemQueueReader, AzureStorageWorkItemQueueReader>();
        services.AddSingleton<IRuntimeRunner, ProcessRuntimeRunner>();
        services.AddScoped<ExecutionService>();
    }
}
