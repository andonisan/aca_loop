namespace Orchestrator.Portal.ReadModel;

/// <summary>
/// One panel's worth of read-model data, or the reason it could not be read. The portal reads several
/// independent Azure and vendor sources per stack, and the spec requires that one failing source
/// degrades its own panel rather than the page — so failure is a value here, not an exception that
/// unwinds a render.
/// </summary>
/// <param name="Value">The data, or <c>default</c> when <paramref name="Unavailable"/> is set.</param>
/// <param name="Unavailable">A short, human-readable reason. Null when the read succeeded.</param>
/// <param name="RetrievedAt">When the underlying read happened — not when this panel was rendered, so
/// a cache hit reports the age of the data it is actually showing.</param>
public sealed record Panel<T>(T? Value, string? Unavailable, DateTimeOffset RetrievedAt)
{
    public bool IsAvailable => Unavailable is null;

    public static Panel<T> Available(T value, DateTimeOffset retrievedAt) =>
        new(value, null, retrievedAt);

    public static Panel<T> Failed(string reason, DateTimeOffset retrievedAt) =>
        new(default, reason, retrievedAt);
}
