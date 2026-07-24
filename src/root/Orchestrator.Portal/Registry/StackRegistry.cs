namespace Orchestrator.Portal.Registry;

/// <summary>The validated, in-memory registry — every field guaranteed present and every referenced
/// mapping file guaranteed to exist on disk, so nothing downstream re-checks it.</summary>
public sealed record StackRegistry(IReadOnlyList<RegisteredStack> Stacks);

/// <summary>
/// One client stack the portal reads. Membership is explicit rather than discovered: the portal
/// concentrates read access across clients, so which stacks it can see is itself an authorization
/// artifact and belongs in a reviewed, versioned file (design D2).
/// </summary>
/// <param name="Id">Stable slug used in URLs and audit events.</param>
/// <param name="DisplayName">Human label for the UI.</param>
/// <param name="MappingFilePath">Resolved path to this stack's bundled mapping file — the same
/// mapping its receiver runs, so the portal resolves actions identically.</param>
public sealed record RegisteredStack(
    string Id,
    string DisplayName,
    string MappingFilePath,
    StackAzureCoordinates Azure
);

/// <param name="QueueServiceUri">Queue service endpoint, e.g. <c>https://acct.queue.core.windows.net</c>.</param>
/// <param name="LogAnalyticsWorkspaceId">The workspace GUID (customer id) run-history queries target.</param>
public sealed record StackAzureCoordinates(
    string SubscriptionId,
    string ResourceGroup,
    Uri QueueServiceUri,
    string QueueName,
    string JobName,
    string LogAnalyticsWorkspaceId
);
