using System.Collections.Concurrent;
using Orchestrator.Portal.Registry;

namespace Orchestrator.Portal.ReadModel;

/// <summary>Reads a source for a stack, applying that source's TTL and converting failure into an
/// unavailable panel.</summary>
public interface IPanelReader
{
    Task<Panel<T>> ReadAsync<T>(
        IStackSource<T> source,
        RegisteredStack stack,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// The single place TTL caching and per-panel degradation are implemented, so every view gets both for
/// free and no source can accidentally ship without them.
/// <para>
/// Failures are cached too, and deliberately: an Azure source that is timing out is exactly the one a
/// refreshing dashboard must not be allowed to hammer. The cached failure expires on the same TTL as a
/// success, so recovery is picked up on the next poll rather than needing an eviction.
/// </para>
/// <para>
/// A plain dictionary rather than <c>IMemoryCache</c>: expiry is then driven by the same
/// <see cref="TimeProvider"/> that stamps <see cref="Panel{T}.RetrievedAt"/>, so there is one clock
/// instead of two that can disagree (and <c>MemoryCacheOptions</c> only exposes the obsolete
/// <c>ISystemClock</c> here). Entries are bounded by sources × registered stacks — both fixed, small,
/// and known at startup — so no eviction policy is needed beyond expiry.
/// </para>
/// </summary>
public sealed class CachedPanelReader(TimeProvider timeProvider, ILogger<CachedPanelReader> logger)
    : IPanelReader
{
    readonly ConcurrentDictionary<string, (object Panel, DateTimeOffset ExpiresAt)> _entries =
        new();

    public async Task<Panel<T>> ReadAsync<T>(
        IStackSource<T> source,
        RegisteredStack stack,
        CancellationToken cancellationToken = default
    )
    {
        var key = $"{source.Name}::{stack.Id}";
        var now = timeProvider.GetUtcNow();

        if (
            _entries.TryGetValue(key, out var entry)
            && entry.ExpiresAt > now
            && entry.Panel is Panel<T> cached
        )
        {
            return cached;
        }

        Panel<T> panel;
        try
        {
            panel = Panel<T>.Available(await source.ReadAsync(stack, cancellationToken), now);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away (page navigated, circuit dropped). Not a source failure, and caching
            // it would misreport a healthy source as broken for a full TTL.
            throw;
        }
        catch (Exception exception)
        {
            PortalLog.PanelSourceFailed(logger, source.Name, stack.Id, exception.Message);
            panel = Panel<T>.Failed(Summarize(exception), now);
        }

        _entries[key] = (panel, now + source.Ttl);
        return panel;
    }

    /// <summary>A one-line reason for the UI. Deliberately the exception's own message rather than a
    /// generic string — an operator reading "unavailable" learns nothing, and these surfaces are
    /// internal-only behind two Entra roles, so there is no untrusted audience to leak shape to.</summary>
    static string Summarize(Exception exception)
    {
        var message = exception.Message.ReplaceLineEndings(" ").Trim();
        return message.Length <= 200 ? message : message[..200] + "…";
    }
}
