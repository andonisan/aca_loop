using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.Dispatch.Contracts;

/// <summary>The matched target plus the matched state within it. Both halves are needed to dispatch:
/// the state supplies action/runtime/failure-state, and the target supplies the code-host coordinates,
/// which belong to the target rather than to any one of its states.</summary>
public sealed record ResolvedTargetState(TargetMapping Target, StateMapping State);

/// <summary>Resolves a canonical event against the loaded, validated mapping config.</summary>
public interface IMappingProvider
{
    /// <summary>Returns false (and no result) when no target matches the vendor/project/repo, or
    /// the target has no entry for <paramref name="nativeState"/> — both are "not a gate state",
    /// not an error (see state-action-config spec's "unmapped state produces no dispatch").</summary>
    bool TryResolve(
        Vendor vendor,
        string? project,
        string repo,
        string nativeState,
        out ResolvedTargetState? resolved
    );
}
