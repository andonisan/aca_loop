namespace Orchestrator.Portal.Registry;

/// <summary>The raw YAML shape of <c>stacks.yaml</c>, before validation. Every property is nullable
/// or defaulted so a malformed file deserializes into something the validator can report on, rather
/// than throwing inside YamlDotNet with no field context.</summary>
sealed class StackRegistryFile
{
    public List<StackRegistryFileEntry> Stacks { get; init; } = [];
}

sealed class StackRegistryFileEntry
{
    public string? Id { get; init; }
    public string? DisplayName { get; init; }
    public string? MappingFile { get; init; }
    public StackRegistryFileAzure? Azure { get; init; }
    public StackRegistryFileVendor? Vendor { get; init; }
}

/// <summary>Which vendor backlog this stack tracks, and where. Required for any vendor read the portal
/// does — the gate-items view and manual dispatch's live-state re-read (design D9). Separate from
/// <see cref="StackRegistryFileAzure"/> because a stack's tracker is not necessarily in Azure at all.</summary>
sealed class StackRegistryFileVendor
{
    /// <summary>Vendor name, matching the <c>Vendor</c> enum: GitHub, GitLab, AzureDevOps, Jira.</summary>
    public string? Name { get; init; }

    /// <summary>Project/organization coordinate, where the vendor has one above the repo (an ADO project,
    /// a Jira project key). Omitted for GitHub.</summary>
    public string? Project { get; init; }

    /// <summary>Repository or project path the tracked items live in. Empty for a tracker whose items are
    /// not repository-scoped, where <see cref="Project"/> identifies them instead.</summary>
    public string? Repo { get; init; }

    /// <summary>
    /// Key Vault secret name holding this stack's <b>read-scoped</b> vendor credential. Named per stack,
    /// not per vendor: two clients on GitHub are two different installations, and the whole point of D9 is
    /// that the portal never reaches for the agent's write-capable secret.
    /// </summary>
    public string? ReadCredentialSecretName { get; init; }
}

sealed class StackRegistryFileAzure
{
    public string? SubscriptionId { get; init; }
    public string? ResourceGroup { get; init; }
    public string? QueueServiceUri { get; init; }
    public string? QueueName { get; init; }
    public string? JobName { get; init; }
    public string? LogAnalyticsWorkspaceId { get; init; }
}
