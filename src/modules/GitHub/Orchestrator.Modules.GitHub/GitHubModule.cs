using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orchestrator.BuildingBlocks.Modules;
using Orchestrator.Modules.Backlog.Contracts;

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

        // Registered as the concrete type once, then forwarded to both contracts, so a single
        // adapter instance serves the tracker and code-host roles. Forwards are transient to match
        // AddHttpClient's lifetime -- a singleton forward would capture a transient HttpClient.
        services.AddHttpClient<GitHubAdapter>(client =>
            client.BaseAddress = new Uri(GitHubApiBaseUrl)
        );
        services.AddTransient<IBacklogAdapter>(sp => sp.GetRequiredService<GitHubAdapter>());
        services.AddTransient<ICodeHostAdapter>(sp => sp.GetRequiredService<GitHubAdapter>());
    }
}
