using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orchestrator.BuildingBlocks.Modules;
using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.Jira;

/// <summary>Registers the Jira tracker adapter. Composed by both Receiver (webhook intake) and Agent
/// (execution) — no HTTP endpoints of its own, mirrors <c>GitHubModule</c>. Registers
/// <see cref="IBacklogAdapter"/> only: Jira hosts no git, so there is no
/// <c>ICodeHostAdapter</c> forward here, and that absence is what makes "Jira cannot be a code host" a
/// compile-and-startup fact rather than a runtime surprise.</summary>
public sealed class JiraModule : WorkerModuleBase
{
    /// <summary>Bounds every outbound call, so a hung Jira cannot hold an agent replica open until the
    /// job's own watchdog fires (design.md D12).</summary>
    static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public override void Add(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JiraOptions>(configuration.GetSection(JiraOptions.SectionName));

        services.AddHttpClient<JiraAdapter>(
            (sp, client) =>
            {
                client.BaseAddress = new Uri(
                    sp.GetRequiredService<IOptions<JiraOptions>>().Value.BaseUrl.TrimEnd('/')
                );
                client.Timeout = RequestTimeout;
            }
        );
        services.AddTransient<IBacklogAdapter>(sp => sp.GetRequiredService<JiraAdapter>());
    }
}
