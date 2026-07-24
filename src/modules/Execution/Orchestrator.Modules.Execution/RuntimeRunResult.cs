namespace Orchestrator.Modules.Execution;

public sealed record RuntimeRunResult(int ExitCode, bool TimedOut, string Output)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}
