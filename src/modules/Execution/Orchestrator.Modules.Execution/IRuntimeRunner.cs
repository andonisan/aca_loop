namespace Orchestrator.Modules.Execution;

public interface IRuntimeRunner
{
    /// <summary>Runs a headless CLI invocation to completion or until <paramref name="timeout"/> fires,
    /// capturing combined stdout/stderr while still streaming it to this process's own logs.
    /// A timeout sends SIGTERM, then SIGKILL after a grace period — matching v1's watchdog.</summary>
    Task<RuntimeRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environment,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    );
}
