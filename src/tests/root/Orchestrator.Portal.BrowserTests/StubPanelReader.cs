using System.Collections.Concurrent;
using Orchestrator.Portal.ReadModel;
using Orchestrator.Portal.Registry;

namespace Orchestrator.Portal.BrowserTests;

/// <summary>
/// Drives every panel's state from the test.
/// <para>
/// Substituted at <see cref="IPanelReader"/> rather than at each source, which is what makes this possible
/// with no production change: the pages inject concrete source types, but every read goes through this one
/// interface, and the reader already treats a source as an opaque thing with a name. So a stub keyed on
/// that name controls the whole read model.
/// </para>
/// <para>
/// It is also the right seam conceptually. <see cref="Panel{T}"/> exists because a failing source has to be
/// a value rather than an exception, and that decision is exactly what lets a test hand the page a failure
/// as easily as a success — including the states that are effectively unproducible against real Azure.
/// </para>
/// </summary>
sealed class StubPanelReader : IPanelReader
{
    readonly ConcurrentDictionary<string, object> _panels = new();

    /// <summary>Panels default to unavailable rather than empty. A test that forgets to arrange a source
    /// should see an obviously unconfigured page, not a plausible empty one it might assert against.</summary>
    public Task<Panel<T>> ReadAsync<T>(
        IStackSource<T> source,
        RegisteredStack stack,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            _panels.TryGetValue(Key(source.Name, stack.Id), out var panel)
            && panel is Panel<T> typed
                ? typed
                : Panel<T>.Failed("no stub arranged for this source", Now)
        );

    public void Available<T>(string sourceName, string stackId, T value) =>
        _panels[Key(sourceName, stackId)] = Panel<T>.Available(value, Now);

    public void Failing<T>(string sourceName, string stackId, string reason) =>
        _panels[Key(sourceName, stackId)] = Panel<T>.Failed(reason, Now);

    public void Reset() => _panels.Clear();

    /// <summary>Fixed rather than the wall clock: the rendered age would otherwise change between runs, and
    /// a test that reads "3s ago" one time and "4s ago" the next is a flake waiting to happen.</summary>
    static DateTimeOffset Now { get; } = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    static string Key(string sourceName, string stackId) => $"{sourceName}::{stackId}";
}
