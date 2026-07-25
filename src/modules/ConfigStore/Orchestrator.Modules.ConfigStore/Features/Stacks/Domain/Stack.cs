using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.ConfigStore.Features.Stacks.Domain;

/// <summary>
/// A registered stack and its dispatch configuration.
/// <para>
/// <c>internal</c>, like every domain entity in ds-connect: consumers get the <c>StoredStack</c> DTO from
/// Contracts, never this. That is what MOD003 enforces once ported, and it is what makes handling the
/// <c>DbContext</c> directly — with no repository layer — safe, since another module physically cannot
/// reference this type.
/// </para>
/// <para>
/// The natural key is the stack id rather than a surrogate. It is already stable, already unique, already
/// what URLs and audit events are keyed on, and a generated id would add a second identity for the same
/// thing — the sort that ends up half-used.
/// </para>
/// </summary>
sealed class Stack
{
    // EF materialization.
    Stack() { }

    public string Id { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;

    public string SubscriptionId { get; private set; } = string.Empty;
    public string ResourceGroup { get; private set; } = string.Empty;
    public string QueueServiceUri { get; private set; } = string.Empty;
    public string QueueName { get; private set; } = string.Empty;
    public string JobName { get; private set; } = string.Empty;
    public string LogAnalyticsWorkspaceId { get; private set; } = string.Empty;

    public Vendor Vendor { get; private set; }
    public string? VendorProject { get; private set; }
    public string VendorRepo { get; private set; } = string.Empty;

    /// <summary>A Key Vault secret name. Never a value — see the schema note on this column.</summary>
    public string ReadCredentialSecretName { get; private set; } = string.Empty;

    public string VendorBaseUrl { get; private set; } = string.Empty;
    public string? VendorDeployment { get; private set; }

    /// <summary>Attribution, not history (design D6). Answers "why does this client dispatch differently
    /// now" well enough to start looking; it does not reconstruct what the value was before.</summary>
    public string LastWrittenBy { get; private set; } = string.Empty;
    public DateTimeOffset LastWrittenAt { get; private set; }

    public List<MappingState> States { get; private set; } = [];
}

/// <summary>One state entry of a stack's mapping. A plain row, not an aggregate: it has no identity or
/// lifecycle apart from the stack that owns it.</summary>
sealed class MappingState
{
    MappingState() { }

    public string StackId { get; private set; } = string.Empty;
    public string NativeState { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public AgentRuntime Runtime { get; private set; }
    public string? OnSuccessState { get; private set; }
    public string OnFailureState { get; private set; } = string.Empty;
    public Vendor CodeHost { get; private set; }
    public string CodeRepo { get; private set; } = string.Empty;

    public string LastWrittenBy { get; private set; } = string.Empty;
    public DateTimeOffset LastWrittenAt { get; private set; }
}
