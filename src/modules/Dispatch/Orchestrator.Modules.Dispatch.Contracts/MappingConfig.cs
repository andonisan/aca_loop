using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.Dispatch.Contracts;

/// <summary>The validated, in-memory form of the mapping file — every field guaranteed present and
/// every enum value guaranteed parseable, so nothing downstream re-checks it.</summary>
public sealed record MappingConfig(IReadOnlyList<TargetMapping> Targets);

public sealed record TargetMapping(
    Vendor Vendor,
    string? Project,
    string Repo,
    IReadOnlyDictionary<string, StateMapping> States
)
{
    public bool Matches(Vendor vendor, string? project, string repo) =>
        Vendor == vendor
        && string.Equals(Repo, repo, StringComparison.OrdinalIgnoreCase)
        && (Project is null || string.Equals(Project, project, StringComparison.OrdinalIgnoreCase));
}

public sealed record StateMapping(
    string Action,
    AgentRuntime Runtime,
    string? OnSuccessState,
    string OnFailureState
);
