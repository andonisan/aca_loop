using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.ConfigStore.Contracts;

/// <summary>
/// A stack as the store holds it. A DTO, not the entity: consumers get this and can never reach the
/// entity type or the <c>DbContext</c> behind it (design D9, and what MOD005 enforces once ported).
/// </summary>
/// <param name="ReadCredentialSecretName">Key Vault secret <b>name</b>. The store holds names, never
/// values — a configuration database that accumulates tokens becomes a credential store with none of Key
/// Vault's properties, and it happens by accretion unless ruled out (design D6).</param>
public sealed record StoredStack(
    string Id,
    string DisplayName,
    StoredAzureCoordinates Azure,
    StoredVendorCoordinates Vendor,
    IReadOnlyList<StoredState> States
);

public sealed record StoredAzureCoordinates(
    string SubscriptionId,
    string ResourceGroup,
    Uri QueueServiceUri,
    string QueueName,
    string JobName,
    string LogAnalyticsWorkspaceId
);

public sealed record StoredVendorCoordinates(
    Vendor Vendor,
    string? Project,
    string Repo,
    string ReadCredentialSecretName,
    Uri BaseUrl,
    string? Deployment
);

/// <param name="NativeState">The vendor-native state that dispatches this action.</param>
public sealed record StoredState(
    string NativeState,
    string Action,
    AgentRuntime Runtime,
    string? OnSuccessState,
    string OnFailureState,
    Vendor CodeHost,
    string CodeRepo
);

/// <summary>
/// Read access to the configuration store, for the hosts that consume configuration rather than edit it.
/// <para>
/// Read-only by construction. The portal's write path is a separate contract, so a receiver — which loads
/// configuration on every start and must never change it — cannot reach an update method at all. Same
/// reasoning as the <c>IBacklogAdapter</c>/<c>IBacklogReader</c> split.
/// </para>
/// </summary>
public interface IConfigStore
{
    /// <summary>Every registered stack. The portal's registry.</summary>
    Task<IReadOnlyList<StoredStack>> ListStacksAsync(CancellationToken cancellationToken = default);

    /// <summary>One stack, or null when it is not registered. A receiver's own configuration.</summary>
    Task<StoredStack?> GetStackAsync(string id, CancellationToken cancellationToken = default);
}
