namespace Orchestrator.Modules.Backlog.Contracts;

/// <summary>
/// A state-change notification normalized from a vendor's native webhook payload. Everything
/// downstream of a vendor adapter (Dispatch, Execution) consumes only this shape — never a
/// vendor-specific payload type.
/// </summary>
/// <param name="Vendor">Which backlog vendor raised this event.</param>
/// <param name="Project">Vendor project/organization coordinate (e.g. an Azure DevOps project, a
/// GitLab namespace). Null where the vendor has no separate project concept above the repo (GitHub).</param>
/// <param name="Repo">The repository (or GitLab project path) the item belongs to.</param>
/// <param name="ItemId">The vendor-native item identifier (issue number, work item id, issue iid).</param>
/// <param name="NativeState">The vendor-native state string that triggered this event (a label name,
/// a work item <c>System.State</c> value, etc.) — not yet resolved against the mapping config.</param>
/// <param name="Actor">The identity that caused the state change, used for automation self-filtering.</param>
public sealed record CanonicalEvent(
    Vendor Vendor,
    string? Project,
    string Repo,
    string ItemId,
    string NativeState,
    string? Actor
);
