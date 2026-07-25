namespace Orchestrator.Portal.ReadModel;

/// <summary>
/// Per-source cache TTLs. Defaults are design D1's: queue depth ~10s, executions ~15s, vendor items
/// ~60s, history ~5min — ordered by how fast the underlying thing actually changes, not by how fast a
/// dashboard could refresh. The point is that repeated page views do not translate 1:1 into Azure and
/// vendor API calls.
/// </summary>
public sealed class ReadModelOptions
{
    public const string SectionName = "Portal:ReadModel";

    public TimeSpan QueueDepthTtl { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan ExecutionsTtl { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Vendor reads are the slowest and the most rate-limited of the four, and a gate state
    /// that changed 30 seconds ago is not operationally urgent.</summary>
    public TimeSpan GatedItemsTtl { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Cross-workspace KQL is the expensive one, over data that is effectively immutable once
    /// written.</summary>
    public TimeSpan HistoryTtl { get; init; } = TimeSpan.FromMinutes(5);
}
