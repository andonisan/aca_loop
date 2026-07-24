namespace Orchestrator.Modules.Execution.UnitTests;

sealed class FakeRuntimeRunner : IRuntimeRunner
{
    public List<string> InvokedFileNames { get; } = [];
    public RuntimeRunResult NextResult { get; set; } = new(0, TimedOut: false, Output: "");

    public Task<RuntimeRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environment,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        InvokedFileNames.Add(fileName);
        return Task.FromResult(NextResult);
    }
}
