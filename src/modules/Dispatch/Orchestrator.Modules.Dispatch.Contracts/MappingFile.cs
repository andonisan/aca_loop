namespace Orchestrator.Modules.Dispatch.Contracts;

/// <summary>The raw YAML shape of the mapping file, before validation/parsing into
/// <see cref="MappingConfig"/>. Field names are camelCase on the wire (YamlDotNet's
/// CamelCaseNamingConvention).</summary>
public sealed class MappingFile
{
    public List<MappingFileTarget> Targets { get; init; } = [];
}

public sealed class MappingFileTarget
{
    public string? Vendor { get; init; }
    public string? Project { get; init; }
    public string? Repo { get; init; }
    public Dictionary<string, MappingFileState> States { get; init; } = [];
}

public sealed class MappingFileState
{
    public string? Action { get; init; }
    public string? Runtime { get; init; }
    public string? OnSuccessState { get; init; }
    public string? OnFailureState { get; init; }
}
