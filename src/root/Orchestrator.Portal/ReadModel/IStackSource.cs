using Orchestrator.Portal.Registry;

namespace Orchestrator.Portal.ReadModel;

/// <summary>
/// One read-model source for one stack. Each source declares its own cache TTL because they have very
/// different costs and volatilities — queue depth is cheap and moves constantly, run history is an
/// expensive cross-workspace query over data that barely changes (design D1).
/// <para>
/// Implementations are free to throw: <see cref="IPanelReader"/> is what turns a failure into an
/// unavailable panel, so no source has to carry its own try/catch or invent an empty result to mean
/// "broken".
/// </para>
/// </summary>
public interface IStackSource<T>
{
    /// <summary>Stable name, used as part of the cache key and in the unavailable message.</summary>
    string Name { get; }

    TimeSpan Ttl { get; }

    Task<T> ReadAsync(RegisteredStack stack, CancellationToken cancellationToken = default);
}
