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
