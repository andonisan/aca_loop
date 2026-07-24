namespace Orchestrator.Modules.Backlog.Contracts;

/// <summary>
/// The tracker half of a vendor: everything needed to observe and write an item's lifecycle state.
/// One implementation per backlog vendor that hosts work items (GitHub, GitLab, Azure DevOps, Jira).
/// Implementations self-register keyed by <see cref="Vendor"/>; resolve the right one for a message
/// via <c>IEnumerable&lt;IBacklogAdapter&gt;</c> + the message's tracker <see cref="Vendor"/>.
/// <para>
/// Deliberately separate from <see cref="ICodeHostAdapter"/>: a tracker need not host git. Jira is a
/// tracker and not a code host, so it implements this interface only — and the absence of a
/// code-host implementation is a compile-time fact rather than a method that throws at runtime
/// (jira-adapter design.md D6).
/// </para>
/// </summary>
public interface IBacklogAdapter
{
    Vendor Vendor { get; }

    /// <summary>Verifies the webhook's authenticity using the vendor's native scheme (HMAC, secret
    /// token header, shared secret). Rejects before any parsing happens.</summary>
    bool VerifyWebhook(ReadOnlyMemory<byte> rawBody, IReadOnlyDictionary<string, string> headers);

    /// <summary>Parses a verified webhook payload into a <see cref="CanonicalEvent"/>, or null if the
    /// payload does not represent a lifecycle-state change (e.g. an unrelated event type/action).</summary>
    CanonicalEvent? ParseEvent(
        ReadOnlyMemory<byte> rawBody,
        IReadOnlyDictionary<string, string> headers
    );

    /// <summary>True when the given actor is this automation's own identity for the vendor (bot
    /// login, service account), so its own write-backs never re-trigger dispatch.</summary>
    bool IsAutomationActor(string? actor);

    /// <summary>Reads the item's current lifecycle state from the vendor's live API — used for gate
    /// re-validation immediately before executing, never trusting the (possibly stale) event payload.</summary>
    Task<string> ReadItemStateAsync(
        string? project,
        string repo,
        string itemId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Writes a state transition back to the vendor using its native mechanism (label swap,
    /// work item state field, workflow transition), preserving the single-state invariant.</summary>
    Task TransitionStateAsync(
        string? project,
        string repo,
        string itemId,
        string newState,
        CancellationToken cancellationToken = default
    );

    Task PostCommentAsync(
        string? project,
        string repo,
        string itemId,
        string body,
        CancellationToken cancellationToken = default
    );
}
