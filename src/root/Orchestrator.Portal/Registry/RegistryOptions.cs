namespace Orchestrator.Portal.Registry;

public sealed class RegistryOptions
{
    public const string SectionName = "Registry";

    public string FilePath { get; init; } = "stacks.yaml";
}
