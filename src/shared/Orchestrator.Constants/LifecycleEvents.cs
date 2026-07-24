namespace Orchestrator.Constants;

/// <summary>
/// The normalized dispatch-lifecycle log contract. Two events — one when Dispatch accepts and
/// enqueues a canonical message, one when an Agent execution reaches any terminal path — carry a
/// fixed set of structured fields so run history can be answered by querying the stack's Log
/// Analytics workspace instead of a datastore this product does not have (portal-read-model spec).
///
/// <para>
/// Because the Portal queries these by name, they are a *contract*, not incidental logging:
/// <see cref="Event"/> identifies the event, <see cref="SchemaVersion"/> is bumped whenever a field
/// is renamed, removed, or given new semantics, and the Portal's queries pin the version they
/// understand so a skewed deploy fails visibly rather than silently returning nothing.
/// </para>
///
/// <para>
/// Reference KQL (workspace-based Application Insights, which is where
/// <c>UseAzureMonitor()</c> in ServiceDefaults exports ILogger records). The table name and the
/// <c>Properties.&lt;Field&gt;</c> filter/extraction shape below were validated against a live
/// .NET workspace using this same ServiceDefaults observability setup; <c>Properties.CategoryName</c>
/// is also present on every row, so queries can discriminate Receiver-emitted from Agent-emitted
/// events without relying on the event name alone. Outcomes for one stack:
/// <code>
/// AppTraces
/// | where Properties.EventName == "dispatch.outcome" and toint(Properties.SchemaVersion) == 1
/// | project TimeGenerated,
///           Vendor = tostring(Properties.Vendor),
///           Repo = tostring(Properties.Repo),
///           ItemId = tostring(Properties.ItemId),
///           Action = tostring(Properties.Action),
///           Runtime = tostring(Properties.Runtime),
///           Outcome = tostring(Properties.Outcome),
///           DurationMs = tolong(Properties.DurationMs),
///           LogPointer = tostring(Properties.LogPointer)
/// | order by TimeGenerated desc
/// </code>
/// Success rate over a window:
/// <code>
/// AppTraces
/// | where TimeGenerated > ago(7d)
/// | where Properties.EventName == "dispatch.outcome" and toint(Properties.SchemaVersion) == 1
/// | summarize Runs = count(),
///             Succeeded = countif(tostring(Properties.Outcome) == "succeeded")
/// | extend SuccessRate = todouble(Succeeded) / todouble(Runs)
/// </code>
/// <see cref="LifecycleOutcomes.LogPointerEnvironmentVariable"/> explains how to get from a row
/// to its console logs.
/// </para>
/// </summary>
public static class LifecycleEvents
{
    /// <summary>Bump when a field is renamed, removed, or re-meaninged. Portal queries pin this.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Dispatch accepted a webhook and enqueued a canonical message.</summary>
    public const string Enqueued = "dispatch.enqueued";

    /// <summary>An Agent execution reached a terminal path. Exactly one per execution that dequeued.</summary>
    public const string Outcome = "dispatch.outcome";
}

/// <summary>
/// The closed set of terminal outcomes for one Agent execution. Every path that ends an execution
/// after it has dequeued a message maps to exactly one of these — including the ones that are
/// neither success nor failure, so that no run is invisible to the read model.
/// </summary>
public static class LifecycleOutcomes
{
    /// <summary>The mapped command ran and exited 0. Any state write-back was the repo command's own.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>The run failed (clone, timeout, non-zero exit, or an unusable message) and the failure contract ran.</summary>
    public const string Failed = "failed";

    /// <summary>The item no longer carried the gate state on re-validation — nothing to do, no command run.</summary>
    public const string StaleGate = "stale-gate";

    /// <summary>The message exceeded the dequeue limit (or named no known vendor) and was dropped unrun.</summary>
    public const string Poison = "poison";

    /// <summary>The command ran and declined because the target repo's WIP limit was reached; the gate state was kept.</summary>
    public const string WipRefused = "wip-refused";

    /// <summary>
    /// The closed set, enumerable. The doc comment on this class calls the outcomes a closed set;
    /// without this, nothing could verify that claim — reflection over the class cannot distinguish an
    /// outcome constant from the log-pointer constants below it. Consumers that must handle every
    /// outcome (the Portal's status mapping) test against this, so adding an outcome above without
    /// handling it downstream fails a test rather than rendering as an unstyled string.
    /// </summary>
    public static readonly string[] All = [Succeeded, Failed, StaleGate, Poison, WipRefused];

    /// <summary>
    /// Name of the environment variable Azure Container Apps injects with the job execution name.
    /// It is the log-tail pointer on an outcome event: given it, the console logs for that run are
    /// <c>ContainerAppConsoleLogs_CL | where ContainerGroupName_s startswith "&lt;pointer&gt;"</c>.
    /// Absent outside ACA (local runs), where the pointer falls back to <see cref="LocalLogPointer"/>.
    /// </summary>
    public const string LogPointerEnvironmentVariable = "CONTAINER_APP_JOB_EXECUTION_NAME";

    /// <summary>Log-pointer value for runs outside Azure Container Apps.</summary>
    public const string LocalLogPointer = "local";
}
