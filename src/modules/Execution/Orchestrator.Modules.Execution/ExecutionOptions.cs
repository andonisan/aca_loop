namespace Orchestrator.Modules.Execution;

/// <summary>
/// v1-parity execution configuration (README.md: <c>replica_timeout_seconds</c>, <c>agent_max_turns</c>,
/// the WIP-refusal pattern, the watchdog buffer). The failure write-back target is no longer
/// configured here — it travels per-message as <c>CanonicalMessage.OnFailureState</c>, resolved by
/// Dispatch from the state-action-config mapping (tasks.md group 2).
/// </summary>
public sealed class ExecutionOptions
{
    public const string SectionName = "Execution";

    public required int ReplicaTimeoutSeconds { get; init; }
    public int WatchdogBufferSeconds { get; init; } = 120;
    public int MaxDequeueCount { get; init; } = 3;
    public string AllowedTools { get; init; } =
        "Bash,Edit,Write,Read,Glob,Grep,WebFetch,WebSearch,TodoWrite,Task";
    public int MaxTurns { get; init; } = 80;
    public string WipRefusalPattern { get; init; } = "WIP limit";
}
