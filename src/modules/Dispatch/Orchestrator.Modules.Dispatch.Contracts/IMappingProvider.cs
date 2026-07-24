using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.Dispatch.Contracts;

/// <summary>Resolves a canonical event against the loaded, validated mapping config.</summary>
public interface IMappingProvider
{
    /// <summary>Returns false (and no mapping) when no target matches the vendor/project/repo, or
    /// the target has no entry for <paramref name="nativeState"/> — both are "not a gate state",
    /// not an error (see state-action-config spec's "unmapped state produces no dispatch").</summary>
    bool TryResolve(
        Vendor vendor,
        string? project,
        string repo,
        string nativeState,
        out StateMapping? mapping
    );
}
