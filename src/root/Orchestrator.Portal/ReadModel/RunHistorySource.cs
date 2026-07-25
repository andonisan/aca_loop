using Azure.Core;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Options;
using Orchestrator.Constants;
using Orchestrator.Portal.Registry;

namespace Orchestrator.Portal.ReadModel;

/// <summary>
/// A stack's run history, answered by querying that stack's Log Analytics workspace for the normalized
/// <c>dispatch.outcome</c> events. The portal persists nothing: history depth is workspace retention, and
/// an outcome that happened while the portal was down still appears, because there is no portal-side
/// ingestion to have missed it (portal-read-model spec).
/// <para>
/// The KQL is the reference query documented on <see cref="LifecycleEvents"/>, including its
/// <c>SchemaVersion</c> pin — a stack deployed with a newer event schema returns nothing here rather than
/// silently mismatching fields, which is the visible failure that pin exists to produce.
/// </para>
/// <para>
/// The window is part of <see cref="Name"/>, so each selectable window caches independently instead of a
/// 7-day view serving stale rows to a 24-hour one.
/// </para>
/// </summary>
public sealed class RunHistorySource(
    TokenCredential credential,
    IOptions<ReadModelOptions> options,
    TimeSpan window
) : IStackSource<RunHistory>
{
    public string Name => $"run-history::{window.TotalMinutes:F0}m";

    public TimeSpan Ttl => options.Value.HistoryTtl;

    public async Task<RunHistory> ReadAsync(
        RegisteredStack stack,
        CancellationToken cancellationToken = default
    )
    {
        var client = new LogsQueryClient(credential);
        var range = new QueryTimeRange(window);

        var runs = await client.QueryWorkspaceAsync<HistoryRow>(
            stack.Azure.LogAnalyticsWorkspaceId,
            $"""
            AppTraces
            | where Properties.EventName == "{LifecycleEvents.Outcome}"
                and toint(Properties.SchemaVersion) == {LifecycleEvents.SchemaVersion}
            | project TimeGenerated,
                      Vendor = tostring(Properties.Vendor),
                      Repo = tostring(Properties.Repo),
                      ItemId = tostring(Properties.ItemId),
                      Action = tostring(Properties.Action),
                      Runtime = tostring(Properties.Runtime),
                      Outcome = tostring(Properties.Outcome),
                      DurationMs = tolong(Properties.DurationMs),
                      LogPointer = tostring(Properties.LogPointer)
            | order by TimeGenerated desc
            | take {options.Value.HistoryRuns}
            """,
            range,
            cancellationToken: cancellationToken
        );

        // A separate aggregate over the whole window: the list above is capped for display, so counting
        // it would report the most recent N runs' rate as the window's rate.
        var totals = await client.QueryWorkspaceAsync<TotalsRow>(
            stack.Azure.LogAnalyticsWorkspaceId,
            $"""
            AppTraces
            | where Properties.EventName == "{LifecycleEvents.Outcome}"
                and toint(Properties.SchemaVersion) == {LifecycleEvents.SchemaVersion}
            | summarize Runs = count(),
                        Succeeded = countif(tostring(Properties.Outcome) == "{LifecycleOutcomes.Succeeded}")
            """,
            range,
            cancellationToken: cancellationToken
        );

        var total = totals.Value.FirstOrDefault();

        return new RunHistory(
            window,
            [
                .. runs.Value.Select(r => new RunHistoryEntry(
                    r.TimeGenerated,
                    r.Vendor,
                    string.IsNullOrEmpty(r.Repo) ? null : r.Repo,
                    r.ItemId,
                    r.Action,
                    r.Runtime,
                    r.Outcome,
                    TimeSpan.FromMilliseconds(r.DurationMs),
                    r.LogPointer
                )),
            ],
            total?.Runs ?? 0,
            total?.Succeeded ?? 0
        );
    }

    /// <summary>Column-name-mapped row shapes for the two queries. Settable properties with a
    /// parameterless constructor, which is what the Monitor SDK's typed overload binds to.</summary>
    sealed class HistoryRow
    {
        public DateTimeOffset TimeGenerated { get; set; }
        public string Vendor { get; set; } = "";
        public string Repo { get; set; } = "";
        public string ItemId { get; set; } = "";
        public string Action { get; set; } = "";
        public string Runtime { get; set; } = "";
        public string Outcome { get; set; } = "";
        public long DurationMs { get; set; }
        public string LogPointer { get; set; } = "";
    }

    sealed class TotalsRow
    {
        public int Runs { get; set; }
        public int Succeeded { get; set; }
    }
}
