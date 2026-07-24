namespace Orchestrator.Modules.Backlog.Contracts;

/// <summary>
/// A state-change notification normalized from a vendor's native webhook payload. Everything
/// downstream of a vendor adapter (Dispatch, Execution) consumes only this shape — never a
/// vendor-specific payload type.
/// <para>
/// Carries <b>tracker</b> coordinates only. Code-host coordinates are deliberately absent: an adapter
/// parsing a webhook knows which tracker raised it but not which git repository the target is
/// configured against — that is mapping configuration, resolved by Dispatch after parsing and merged
/// into <see cref="CanonicalMessage"/>. Putting them here would force a tracker-only adapter such as
/// Jira to invent values it cannot know (jira-adapter design.md D6).
/// </para>
/// </summary>
/// <param name="Vendor">Which backlog vendor raised this event (the tracker).</param>
/// <param name="Project">Vendor project/organization coordinate (e.g. an Azure DevOps project, a
/// GitLab namespace, a Jira project key). Null where the vendor has no separate project concept above
/// the repo (GitHub).</param>
/// <param name="Repo">The repository (or GitLab project path) the item belongs to. Empty for a
/// tracker whose items are not repository-scoped, where <paramref name="Project"/> identifies the
/// item's container instead.</param>
/// <param name="ItemId">The vendor-native item identifier (issue number, work item id, issue iid,
/// Jira issue key).</param>
/// <param name="NativeState">The vendor-native state string that triggered this event (a label name,
/// a work item <c>System.State</c> value, a Jira status name) — not yet resolved against the mapping
/// config.</param>
/// <param name="Actor">The identity that caused the state change, used for automation self-filtering.</param>
public sealed record CanonicalEvent(
    Vendor Vendor,
    string? Project,
    string Repo,
    string ItemId,
    string NativeState,
    string? Actor
);
