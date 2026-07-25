namespace Orchestrator.Modules.Backlog.Contracts;

/// <summary>
/// A work item as a read-only consumer sees it. Distinct from <see cref="CanonicalEvent"/>: that describes
/// a transition that just happened, this describes an item sitting in a state right now.
/// </summary>
/// <param name="ItemId">Vendor-native identifier — issue number, work item id, issue iid, Jira issue key.</param>
/// <param name="NativeState">The vendor-native state the item currently carries.</param>
/// <param name="Url">Deep link to the item on the vendor, so a listing is actionable rather than a set of
/// identifiers an operator has to reconstruct a URL from.</param>
public sealed record BacklogItem(
    string ItemId,
    string Title,
    string NativeState,
    string? Assignee,
    DateTimeOffset? UpdatedAt,
    Uri? Url
);

/// <summary>
/// Read-only access to a vendor's backlog, for consumers that must never write — today the Portal.
/// <para>
/// Deliberately not methods on <see cref="IBacklogAdapter"/>, for two reasons (orchestrator-portal
/// design D9). First, the adapter answers "what state is item X in" and this answers "which items are in
/// state X" — the inverse question, which no adapter can serve. Second and more important: adapters are
/// constructed from write-capable vendor credentials, and the Portal is required to use read-scoped ones.
/// A separate contract makes "this consumer cannot write" something the compiler enforces instead of
/// something a deployment promises.
/// </para>
/// <para>
/// Implementations are therefore expected to be constructed from read-scoped options — a read-only GitHub
/// installation token, a GitLab <c>read_api</c> token, a read-scoped ADO PAT, a Jira account with browse
/// permission only — and to expose no operation that could mutate the vendor.
/// </para>
/// </summary>
public interface IBacklogReader
{
    Vendor Vendor { get; }

    /// <summary>
    /// The items currently carrying any of <paramref name="states"/>.
    /// <para>
    /// Takes the whole set rather than being called once per state so an implementation can answer in one
    /// vendor query where the vendor allows it (a single JQL/WIQL/label filter), which matters because
    /// these are the slowest and most rate-limited reads the Portal makes. Items in states outside the set
    /// MUST NOT be returned: the caller's set is the mapping's gate states, and an unmapped state
    /// appearing in a gate view would misrepresent what the loop acts on.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<BacklogItem>> ListItemsInStatesAsync(
        string? project,
        string repo,
        IReadOnlyCollection<string> states,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// One item's current state, read live.
    /// <para>
    /// The same question <c>IBacklogAdapter.ReadItemStateAsync</c> answers, deliberately duplicated onto
    /// the read-only contract rather than shared. A consumer that must not write needs this read, and
    /// giving it the adapter to get it would hand it the transition and comment methods too — which is
    /// exactly what the two contracts exist to prevent (orchestrator-portal design D9).
    /// </para>
    /// <para>
    /// Returns empty when the item carries no state the vendor exposes as one, matching the adapter's
    /// behaviour so both sides of a gate comparison mean the same thing.
    /// </para>
    /// </summary>
    Task<string> ReadItemStateAsync(
        string? project,
        string repo,
        string itemId,
        CancellationToken cancellationToken = default
    );
}
