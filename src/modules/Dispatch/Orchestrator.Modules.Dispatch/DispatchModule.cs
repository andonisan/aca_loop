using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orchestrator.BuildingBlocks.Modules;
using Orchestrator.Modules.Backlog.Contracts;
using Orchestrator.Modules.Dispatch.Contracts;

namespace Orchestrator.Modules.Dispatch;

/// <summary>Composed only by the Receiver host — owns the vendor-agnostic webhook endpoint and the
/// enqueue side of the AI-pod dispatch pipeline.</summary>
public sealed class DispatchModule : EndpointModuleBase
{
    public override void Add(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<QueueOptions>(configuration.GetSection(QueueOptions.SectionName));

        services.AddSingleton(sp =>
            QueueClientFactory.Create(sp.GetRequiredService<IOptions<QueueOptions>>())
        );
        services.AddSingleton<IWorkItemQueue, AzureStorageWorkItemQueue>();

        // Loaded and validated eagerly, here, before the host finishes building — an invalid
        // mapping throws and the process never reaches app.Run(), satisfying "fail fast, no
        // traffic served" (state-action-config spec).
        var mappingOptions = new MappingOptions();
        configuration.GetSection(MappingOptions.SectionName).Bind(mappingOptions);
        var mappingConfig = MappingLoader.LoadFromFile(mappingOptions.FilePath);
        services.AddSingleton(mappingConfig);
        services.AddSingleton<IMappingProvider>(new MappingProvider(mappingConfig));
    }

    /// <summary>
    /// Checks that every target's code host has a registered <see cref="ICodeHostAdapter"/>. This
    /// cannot happen in <see cref="Add"/> like the rest of mapping validation: adapters are registered
    /// by factory, so their vendor is unknowable until the container exists. Running here still
    /// precedes <c>app.Run()</c>, so a violation refuses traffic rather than surfacing as a failed run
    /// hours later (state-action-config spec, "code-host resolution is validated at startup").
    /// <para>
    /// The case this catches today is not Jira but a disabled vendor: a target naming
    /// <c>codeHost: GitLab</c> in a deployment where the GitLab module was never composed.
    /// </para>
    /// </summary>
    public override Task InitializeAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default
    )
    {
        var config = serviceProvider.GetRequiredService<MappingConfig>();
        var registered = serviceProvider
            .GetServices<ICodeHostAdapter>()
            .Select(a => a.Vendor)
            .ToHashSet();

        var violations = config
            .Targets.Where(t => !registered.Contains(t.CodeHost))
            .Select(t =>
                $"target {t.Vendor}/{t.Project ?? "-"}/{(t.Repo.Length == 0 ? "-" : t.Repo)} "
                + $"declares codeHost '{t.CodeHost}', which has no registered code-host adapter"
            )
            .ToList();

        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                "Invalid mapping configuration:\n"
                    + string.Join('\n', violations.Select(v => $"  - {v}"))
            );
        }

        return Task.CompletedTask;
    }
}
