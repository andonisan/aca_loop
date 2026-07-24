using Orchestrator.Modules.Backlog.Contracts;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Orchestrator.Modules.Dispatch.Contracts;

/// <summary>
/// Loads and validates the state-action-config mapping file. Validation is structural (required
/// fields present, enum values parseable) rather than a JSON-Schema-library check — the schema is
/// small enough that hand-written checks give the same fail-fast guarantee with one fewer
/// dependency; every violation is collected before throwing so a misconfigured deploy sees the
/// full list in one pass, not one error per restart.
/// </summary>
public static class MappingLoader
{
    public static MappingConfig LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Mapping file not found: {path}");
        }

        return Parse(File.ReadAllText(path));
    }

    public static MappingConfig Parse(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var file = deserializer.Deserialize<MappingFile>(yaml) ?? new MappingFile();

        var errors = new List<string>();
        var targets = new List<TargetMapping>();

        if (file.Targets.Count == 0)
        {
            errors.Add("mapping must declare at least one target");
        }

        for (var i = 0; i < file.Targets.Count; i++)
        {
            var raw = file.Targets[i];
            var prefix = $"targets[{i}]";

            if (string.IsNullOrWhiteSpace(raw.Vendor))
            {
                errors.Add($"{prefix}.vendor is required");
            }
            else if (!Enum.TryParse<Vendor>(raw.Vendor, ignoreCase: true, out var vendor))
            {
                errors.Add($"{prefix}.vendor '{raw.Vendor}' is not a known vendor");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(raw.Repo))
                {
                    errors.Add($"{prefix}.repo is required");
                }

                if (raw.States.Count == 0)
                {
                    errors.Add($"{prefix}.states must declare at least one state");
                }

                var states = new Dictionary<string, StateMapping>();
                foreach (var (nativeState, rawState) in raw.States)
                {
                    var statePrefix = $"{prefix}.states['{nativeState}']";

                    if (string.IsNullOrWhiteSpace(rawState.Action))
                    {
                        errors.Add($"{statePrefix}.action is required");
                    }
                    if (string.IsNullOrWhiteSpace(rawState.OnFailureState))
                    {
                        errors.Add($"{statePrefix}.onFailureState is required");
                    }

                    AgentRuntime runtime = default;
                    if (string.IsNullOrWhiteSpace(rawState.Runtime))
                    {
                        errors.Add($"{statePrefix}.runtime is required");
                    }
                    else if (!Enum.TryParse(rawState.Runtime, ignoreCase: true, out runtime))
                    {
                        errors.Add(
                            $"{statePrefix}.runtime '{rawState.Runtime}' is not a known runtime"
                        );
                    }

                    if (
                        !string.IsNullOrWhiteSpace(rawState.Action)
                        && !string.IsNullOrWhiteSpace(rawState.OnFailureState)
                    )
                    {
                        states[nativeState] = new StateMapping(
                            rawState.Action,
                            runtime,
                            rawState.OnSuccessState,
                            rawState.OnFailureState
                        );
                    }
                }

                if (!string.IsNullOrWhiteSpace(raw.Repo))
                {
                    targets.Add(new TargetMapping(vendor, raw.Project, raw.Repo, states));
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Invalid mapping configuration:\n"
                    + string.Join('\n', errors.Select(e => $"  - {e}"))
            );
        }

        return new MappingConfig(targets);
    }
}
