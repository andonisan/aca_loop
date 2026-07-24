using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orchestrator.BuildingBlocks.Modules;
using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.GitLab;

/// <summary>Registers the GitLab vendor adapter. Composed by both Receiver (webhook intake) and
/// Agent (execution) — no HTTP endpoints of its own, mirrors <c>GitHubModule</c>.</summary>
public sealed class GitLabModule : WorkerModuleBase
{
    public override void Add(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GitLabOptions>(configuration.GetSection(GitLabOptions.SectionName));

        // One instance, both contracts -- see GitHubModule for why the forwards are transient.
        services.AddHttpClient<GitLabAdapter>(
            (sp, client) =>
                client.BaseAddress = new Uri(
                    $"{sp.GetRequiredService<IOptions<GitLabOptions>>().Value.BaseUrl.TrimEnd('/')}/api/v4/"
                )
        );
        services.AddTransient<IBacklogAdapter>(sp => sp.GetRequiredService<GitLabAdapter>());
        services.AddTransient<ICodeHostAdapter>(sp => sp.GetRequiredService<GitLabAdapter>());
    }
}
