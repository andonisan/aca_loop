using Orchestrator.BuildingBlocks.Modules;
using Orchestrator.Modules.Backlog.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Orchestrator.Modules.GitHub;

/// <summary>Registers the GitHub vendor adapter. Composed by both Receiver (webhook intake) and
/// Agent (execution) — it exposes no HTTP endpoints itself, so it derives from
/// <see cref="WorkerModuleBase"/> rather than <see cref="EndpointModuleBase"/>.</summary>
public sealed class GitHubModule : WorkerModuleBase
{
    const string GitHubApiBaseUrl = "https://api.github.com";

    public override void Add(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GitHubOptions>(configuration.GetSection(GitHubOptions.SectionName));

        services.AddHttpClient<IVendorAdapter, GitHubAdapter>(client =>
            client.BaseAddress = new Uri(GitHubApiBaseUrl)
        );
    }
}
