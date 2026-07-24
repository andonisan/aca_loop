namespace Orchestrator.Modules.Dispatch;

/// <summary>Where the state-action-config mapping file lives. Deployed with the orchestrator (baked
/// or mounted), never read from a target repo — see design.md D2.</summary>
public sealed class MappingOptions
{
    public const string SectionName = "Mapping";

    public string FilePath { get; init; } = "mapping.yaml";
}
