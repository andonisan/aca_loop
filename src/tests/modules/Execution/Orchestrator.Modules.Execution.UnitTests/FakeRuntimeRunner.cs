namespace Orchestrator.Modules.Execution.UnitTests;

sealed class FakeRuntimeRunner : IRuntimeRunner
{
    public List<string> InvokedFileNames { get; } = [];

    /// <summary>The environment of the most recent invocation, so tests can assert which CLI token
    /// the code-host adapter contributed.</summary>
    public IReadOnlyDictionary<string, string?> LastEnvironment { get; private set; } =
        new Dictionary<string, string?>();

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
        LastEnvironment = environment;
        return Task.FromResult(NextResult);
    }
}
