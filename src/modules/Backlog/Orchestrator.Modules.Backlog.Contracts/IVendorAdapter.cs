namespace Orchestrator.Modules.Backlog.Contracts;

/// <summary>
/// One implementation per backlog vendor (GitHub, GitLab, Azure DevOps). Everything a vendor
/// module needs to expose to be usable by Dispatch (Receiver) and Execution (Agent) — see
/// design.md D1. Implementations self-register their <c>IModule</c> keyed by <see cref="Vendor"/>;
/// resolve the right adapter for a message via <c>IEnumerable&lt;IVendorAdapter&gt;</c> + <see cref="Vendor"/>.
/// </summary>
public interface IVendorAdapter
{
    Vendor Vendor { get; }

    /// <summary>Verifies the webhook's authenticity using the vendor's native scheme (HMAC, secret
    /// token header, shared secret). Rejects before any parsing happens.</summary>
    bool VerifyWebhook(ReadOnlyMemory<byte> rawBody, IReadOnlyDictionary<string, string> headers);

    /// <summary>Parses a verified webhook payload into a <see cref="CanonicalEvent"/>, or null if the
    /// payload does not represent a lifecycle-state change (e.g. an unrelated event type/action).</summary>
    CanonicalEvent? ParseEvent(ReadOnlyMemory<byte> rawBody, IReadOnlyDictionary<string, string> headers);

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
    /// work item state field), preserving the single-state invariant.</summary>
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

    /// <summary>Mints short-lived, per-run credentials for cloning and pushing to the target repo.</summary>
    Task<CloneCredentials> GetCloneCredentialsAsync(
        string? project,
        string repo,
        CancellationToken cancellationToken = default
    );
}
