using Orchestrator.BuildingBlocks.Modules;
using Orchestrator.Modules.Backlog.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Orchestrator.Modules.AzureDevOps;

/// <summary>Registers the Azure DevOps vendor adapter. Composed by both Receiver (webhook intake)
/// and Agent (execution) — no HTTP endpoints of its own, mirrors <c>GitHubModule</c>.</summary>
public sealed class AzureDevOpsModule : WorkerModuleBase
{
    public override void Add(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AzureDevOpsOptions>(configuration.GetSection(AzureDevOpsOptions.SectionName));

        services.AddHttpClient<IVendorAdapter, AzureDevOpsAdapter>(
            (sp, client) =>
                client.BaseAddress = new Uri(
                    $"https://dev.azure.com/{sp.GetRequiredService<IOptions<AzureDevOpsOptions>>().Value.Organization}"
                )
        );
    }
}
