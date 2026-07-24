using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Orchestrator.Modules.Execution;

/// <summary>Ported from v1's <c>runAgent</c> (agent/src/entrypoint.js): spawns the CLI, captures
/// combined output while streaming it through, and enforces the watchdog timeout by killing the
/// process tree. Unlike v1's SIGTERM-then-SIGKILL grace period, <see cref="Process.Kill(bool)"/> is
/// used directly — the timeout deadline is what matters for the failure contract, not the exact
/// signal sequence.</summary>
sealed class ProcessRuntimeRunner : IRuntimeRunner
{
    public async Task<RuntimeRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environment,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }
        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var output = new StringBuilder();
        var outputLock = new Lock();

        void Capture(string? data, TextWriter sink)
        {
            if (data is null)
            {
                return;
            }
            sink.WriteLine(data);
            lock (outputLock)
            {
                output.AppendLine(data);
            }
        }

        process.OutputDataReceived += (_, e) => Capture(e.Data, Console.Out);
        process.ErrorDataReceived += (_, e) => Capture(e.Data, Console.Error);

        if (!process.Start())
        {
            return new RuntimeRunResult(-1, TimedOut: false, $"failed to spawn {fileName}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token,
            cancellationToken
        );

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = timeoutCts.IsCancellationRequested;
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        return new RuntimeRunResult(timedOut ? -1 : process.ExitCode, timedOut, output.ToString());
    }

    static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited between the timeout firing and the kill attempt.
        }
    }
}
