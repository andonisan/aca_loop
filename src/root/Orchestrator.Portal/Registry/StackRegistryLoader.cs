using Orchestrator.Modules.Backlog.Contracts;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Orchestrator.Portal.Registry;

/// <summary>
/// Loads and validates <c>stacks.yaml</c>. Structural validation by hand rather than a JSON-Schema
/// library, matching the mapping loader's call in the receiver: the schema is small, and collecting
/// every violation before throwing means a misconfigured deploy sees the full list in one pass
/// instead of one error per restart.
/// </summary>
static class StackRegistryLoader
{
    public static StackRegistry LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Stack registry file not found: {path}");
        }

        // Mapping paths in the file are relative to the registry itself, so the pair can be bundled
        // and moved together.
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";

        return Parse(File.ReadAllText(path), baseDirectory);
    }

    public static StackRegistry Parse(string yaml, string baseDirectory)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var file = deserializer.Deserialize<StackRegistryFile>(yaml) ?? new StackRegistryFile();

        var errors = new List<string>();
        var stacks = new List<RegisteredStack>();

        if (file.Stacks.Count == 0)
        {
            errors.Add("registry must declare at least one stack");
        }

        for (var i = 0; i < file.Stacks.Count; i++)
        {
            var raw = file.Stacks[i];
            var prefix = $"stacks[{i}]";

            var id = Required(raw.Id, $"{prefix}.id", errors);
            var displayName = raw.DisplayName;
            var mappingFile = Required(raw.MappingFile, $"{prefix}.mappingFile", errors);

            string? mappingPath = null;
            if (mappingFile is not null)
            {
                mappingPath = Path.IsPathRooted(mappingFile)
                    ? mappingFile
                    : Path.Combine(baseDirectory, mappingFile);

                // A registry entry whose mapping is absent would resolve no action at dispatch time,
                // so it is a startup failure rather than a per-request surprise.
                if (!File.Exists(mappingPath))
                {
                    errors.Add(
                        $"{prefix}.mappingFile '{mappingFile}' does not exist (looked in {mappingPath})"
                    );
                }
            }

            var azure = raw.Azure;
            if (azure is null)
            {
                errors.Add($"{prefix}.azure is required");
                continue;
            }

            var subscriptionId = Required(
                azure.SubscriptionId,
                $"{prefix}.azure.subscriptionId",
                errors
            );
            var resourceGroup = Required(
                azure.ResourceGroup,
                $"{prefix}.azure.resourceGroup",
                errors
            );
            var queueName = Required(azure.QueueName, $"{prefix}.azure.queueName", errors);
            var jobName = Required(azure.JobName, $"{prefix}.azure.jobName", errors);
            var workspaceId = Required(
                azure.LogAnalyticsWorkspaceId,
                $"{prefix}.azure.logAnalyticsWorkspaceId",
                errors
            );

            Uri? queueServiceUri = null;
            var rawQueueUri = Required(
                azure.QueueServiceUri,
                $"{prefix}.azure.queueServiceUri",
                errors
            );
            if (
                rawQueueUri is not null
                && !Uri.TryCreate(rawQueueUri, UriKind.Absolute, out queueServiceUri)
            )
            {
                errors.Add(
                    $"{prefix}.azure.queueServiceUri '{rawQueueUri}' is not an absolute URI"
                );
            }

            // Vendor coordinates are required, not optional: every read the portal does against a tracker
            // needs them, and a stack that cannot be read is not a stack this portal can show (design D9).
            var rawVendor = raw.Vendor;
            if (rawVendor is null)
            {
                errors.Add($"{prefix}.vendor is required");
                continue;
            }

            Vendor vendor = default;
            var vendorName = Required(rawVendor.Name, $"{prefix}.vendor.name", errors);
            if (vendorName is not null && !Enum.TryParse(vendorName, ignoreCase: true, out vendor))
            {
                errors.Add($"{prefix}.vendor.name '{vendorName}' is not a known vendor");
                vendorName = null;
            }

            var readSecret = Required(
                rawVendor.ReadCredentialSecretName,
                $"{prefix}.vendor.readCredentialSecretName",
                errors
            );

            // Same rule the mapping loader applies to a target: at least one coordinate must identify the
            // items, since a non-repo-anchored tracker (a Jira project) has no repo of its own.
            if (
                string.IsNullOrWhiteSpace(rawVendor.Repo)
                && string.IsNullOrWhiteSpace(rawVendor.Project)
            )
            {
                errors.Add($"{prefix}.vendor must declare at least one of repo/project");
                readSecret = null;
            }

            // GitHub's API address is fixed; every other vendor is per-instance and the portal serves
            // stacks on different instances, so it cannot come from a per-vendor setting.
            Uri? vendorBaseUrl = null;
            var rawBaseUrl = string.IsNullOrWhiteSpace(rawVendor.BaseUrl)
                ? vendor == Vendor.GitHub
                    ? "https://api.github.com"
                    : null
                : rawVendor.BaseUrl;
            if (rawBaseUrl is null)
            {
                errors.Add($"{prefix}.vendor.baseUrl is required for {vendor}");
            }
            else if (!Uri.TryCreate(rawBaseUrl, UriKind.Absolute, out vendorBaseUrl))
            {
                errors.Add($"{prefix}.vendor.baseUrl '{rawBaseUrl}' is not an absolute URI");
            }

            // Jira's deployment decides REST version, comment format and auth together, so a wrong value
            // fails on the failure path -- validate it here rather than discover it then.
            if (
                vendor == Vendor.Jira
                && !string.IsNullOrWhiteSpace(rawVendor.Deployment)
                && rawVendor.Deployment is not ("Cloud" or "DataCenter")
            )
            {
                errors.Add(
                    $"{prefix}.vendor.deployment '{rawVendor.Deployment}' must be Cloud or DataCenter"
                );
                vendorBaseUrl = null;
            }

            if (
                id is null
                || mappingPath is null
                || subscriptionId is null
                || resourceGroup is null
                || queueServiceUri is null
                || queueName is null
                || jobName is null
                || workspaceId is null
                || vendorName is null
                || readSecret is null
                || vendorBaseUrl is null
            )
            {
                continue;
            }

            stacks.Add(
                new RegisteredStack(
                    id,
                    string.IsNullOrWhiteSpace(displayName) ? id : displayName,
                    mappingPath,
                    new StackAzureCoordinates(
                        subscriptionId,
                        resourceGroup,
                        queueServiceUri,
                        queueName,
                        jobName,
                        workspaceId
                    ),
                    new StackVendorCoordinates(
                        vendor,
                        rawVendor.Project,
                        rawVendor.Repo ?? string.Empty,
                        readSecret,
                        vendorBaseUrl,
                        rawVendor.Deployment
                    )
                )
            );
        }

        var duplicateIds = stacks
            .GroupBy(stack => stack.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
        foreach (var duplicate in duplicateIds)
        {
            // Ids key URLs and audit events; two stacks sharing one would make an audit trail
            // ambiguous about which client was touched.
            errors.Add($"duplicate stack id '{duplicate}'");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Invalid stack registry:\n" + string.Join('\n', errors.Select(e => $"  - {e}"))
            );
        }

        return new StackRegistry(stacks);
    }

    static string? Required(string? value, string field, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        errors.Add($"{field} is required");
        return null;
    }
}
