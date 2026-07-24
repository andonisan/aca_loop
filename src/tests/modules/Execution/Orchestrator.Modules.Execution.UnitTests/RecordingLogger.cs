using Microsoft.Extensions.Logging;

namespace Orchestrator.Modules.Execution.UnitTests;

/// <summary>
/// Captures structured log records so the normalized lifecycle contract can be asserted field by
/// field. Hand-rolled rather than taking a fake-logger package dependency for one assertion shape,
/// matching the "one fewer dependency" call made for the mapping validator.
/// </summary>
sealed class RecordingLogger<T> : ILogger<T>
{
    readonly List<RecordedLog> records = [];

    public IReadOnlyList<RecordedLog> Records => records;

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        var fields = state is IReadOnlyList<KeyValuePair<string, object?>> values
            ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
            : [];

        records.Add(new RecordedLog(eventId.Id, fields));
    }

    sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose() { }
    }
}

sealed record RecordedLog(int EventId, IReadOnlyDictionary<string, object?> Fields)
{
    public string? Field(string name) =>
        Fields.TryGetValue(name, out var value) ? value?.ToString() : null;
}
