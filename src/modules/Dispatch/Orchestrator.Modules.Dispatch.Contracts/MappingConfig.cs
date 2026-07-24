using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.Dispatch.Contracts;

/// <summary>The validated, in-memory form of the mapping file — every field guaranteed present and
/// every enum value guaranteed parseable, so nothing downstream re-checks it.</summary>
public sealed record MappingConfig(IReadOnlyList<TargetMapping> Targets);

/// <param name="Repo">The tracker's repository coordinate, or empty for a tracker whose items are not
/// repository-scoped (a Jira project), where <paramref name="Project"/> identifies the target.</param>
/// <param name="CodeHost">Where the git repository lives. Equals <paramref name="Vendor"/> unless the
/// mapping declared otherwise.</param>
/// <param name="CodeRepo">The repository coordinate on <paramref name="CodeHost"/>. Always non-empty —
/// there is nothing to clone otherwise, so the loader rejects a target that resolves to an empty one.</param>
public sealed record TargetMapping(
    Vendor Vendor,
    string? Project,
    string Repo,
    IReadOnlyDictionary<string, StateMapping> States,
    Vendor CodeHost,
    string CodeRepo
)
{
    public bool Matches(Vendor vendor, string? project, string repo)
    {
        if (Vendor != vendor)
        {
            return false;
        }

        // A non-repo-anchored tracker matches on its project coordinate alone: the event carries no
        // repo to compare, and this target's CodeRepo is a coordinate on a different vendor entirely,
        // so it must not participate in matching (state-action-config spec, "Non-repo-anchored
        // targets resolve by tracker coordinate").
        if (Repo.Length == 0)
        {
            return string.Equals(Project, project, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(Repo, repo, StringComparison.OrdinalIgnoreCase)
            && (
                Project is null
                || string.Equals(Project, project, StringComparison.OrdinalIgnoreCase)
            );
    }
}

public sealed record StateMapping(
    string Action,
    AgentRuntime Runtime,
    string? OnSuccessState,
    string OnFailureState
);
