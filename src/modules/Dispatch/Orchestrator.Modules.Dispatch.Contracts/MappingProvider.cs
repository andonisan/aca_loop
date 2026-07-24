using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.Dispatch.Contracts;

/// <summary>Public, unlike the other adapters' implementations: the Portal must resolve a canonical
/// event through the very same resolver the Receiver uses. Duplicating this logic into a second
/// reader is unacceptable for an authorization surface — the two must not be able to drift
/// (orchestrator-portal design.md D8).</summary>
public sealed class MappingProvider(MappingConfig config) : IMappingProvider
{
    public bool TryResolve(
        Vendor vendor,
        string? project,
        string repo,
        string nativeState,
        out StateMapping? mapping
    )
    {
        var target = config.Targets.FirstOrDefault(t => t.Matches(vendor, project, repo));
        if (target is null)
        {
            mapping = null;
            return false;
        }

        return target.States.TryGetValue(nativeState, out mapping);
    }
}
