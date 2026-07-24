using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.Dispatch;

sealed class MappingProvider(MappingConfig config) : IMappingProvider
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
