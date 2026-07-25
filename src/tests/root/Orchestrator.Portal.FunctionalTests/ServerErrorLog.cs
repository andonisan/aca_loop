using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Orchestrator.Portal.FunctionalTests;

/// <summary>
/// Captures server-side errors so a failed assertion can say <i>why</i>.
/// <para>
/// Borrowed from ds-connect, and it earns its place here more than there: this is a Blazor Server app, so
/// a page that throws during render answers 500 and the test reports "expected 200, got 500" — an opaque
/// status with the actual exception sitting in a log nobody reads. Folding the captured errors into the
/// failure message turns a ten-minute hunt into a glance.
/// </para>
/// </summary>
static class ServerErrorLog
{
    static readonly ConcurrentQueue<string> Errors = new();

    public static void Clear() => Errors.Clear();

    public static void Add(string message) => Errors.Enqueue(message);

    /// <summary>The captured errors, or empty when the failure had no server-side cause — which is itself
    /// information: it means the status came from the pipeline rather than from an exception.</summary>
    public static string Summary =>
        Errors.IsEmpty ? "" : "\nServer errors:\n  " + string.Join("\n  ", Errors);
}

/// <summary>Only errors: capturing everything would bury the one line that matters.</summary>
sealed class ServerErrorCaptureProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName);

    public void Dispose() { }

    sealed class CaptureLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            // The exception type and message, not the whole stack: the point is to identify the failure at
            // a glance, and a full trace in an assertion message buries it again.
            var detail = exception is null
                ? ""
                : $" ({exception.GetType().Name}: {exception.Message})";
            ServerErrorLog.Add($"[{category}] {formatter(state, exception)}{detail}");
        }
    }
}
