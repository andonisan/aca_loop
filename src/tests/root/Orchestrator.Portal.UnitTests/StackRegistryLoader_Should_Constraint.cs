using Orchestrator.Portal.Registry;
using Shouldly;

namespace Orchestrator.Portal.UnitTests;

/// <summary>
/// The registry is the portal's authorization artifact — it decides which client stacks this one
/// deployment may read. Every rule here exists so a misconfigured registry stops the process instead
/// of producing a portal that silently sees the wrong set of stacks (portal-read-model spec).
/// </summary>
public sealed class StackRegistryLoader_Should_Constraint : IDisposable
{
    readonly string directory = Directory.CreateTempSubdirectory("portal-registry-tests-").FullName;

    public StackRegistryLoader_Should_Constraint()
    {
        File.WriteAllText(Path.Combine(directory, "present.mapping.yaml"), "targets: []");
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);

    const string ValidYaml = """
        stacks:
          - id: dsconnect-dev
            displayName: ds-connect (dev)
            mappingFile: present.mapping.yaml
            azure:
              subscriptionId: "sub-1"
              resourceGroup: rg-dsconnect-dev
              queueServiceUri: https://stdev.queue.core.windows.net
              queueName: ai-jobs
              jobName: ca-dsconnect-dev-agent
              logAnalyticsWorkspaceId: "workspace-1"
            vendor:
              name: GitHub
              repo: acme/widgets
              readCredentialSecretName: Portal--s--Read
        """;

    StackRegistry Parse(string yaml) => StackRegistryLoader.Parse(yaml, directory);

    string ShouldFail(string yaml) =>
        Should.Throw<InvalidOperationException>(() => Parse(yaml)).Message;

    [Fact]
    public void Parse_Should_ReadAValidRegistry()
    {
        var registry = Parse(ValidYaml);

        var stack = registry.Stacks.ShouldHaveSingleItem();
        stack.Id.ShouldBe("dsconnect-dev");
        stack.DisplayName.ShouldBe("ds-connect (dev)");
        stack.Azure.ResourceGroup.ShouldBe("rg-dsconnect-dev");
        stack.Azure.QueueName.ShouldBe("ai-jobs");
        stack.Azure.JobName.ShouldBe("ca-dsconnect-dev-agent");
        stack.Azure.LogAnalyticsWorkspaceId.ShouldBe("workspace-1");
        stack.Azure.QueueServiceUri.ShouldBe(new Uri("https://stdev.queue.core.windows.net"));
    }

    [Fact]
    public void Parse_Should_ResolveMappingPathsRelativeToTheRegistryFile()
    {
        // The registry and its bundled mappings are deployed together, so a relative path must
        // follow the registry rather than the process's working directory.
        var stack = Parse(ValidYaml).Stacks.ShouldHaveSingleItem();

        stack.MappingFilePath.ShouldBe(Path.Combine(directory, "present.mapping.yaml"));
        File.Exists(stack.MappingFilePath).ShouldBeTrue();
    }

    [Fact]
    public void Parse_Should_DefaultDisplayNameToTheStackId()
    {
        var yaml = ValidYaml.Replace("    displayName: ds-connect (dev)\n", "");

        Parse(yaml).Stacks.ShouldHaveSingleItem().DisplayName.ShouldBe("dsconnect-dev");
    }

    [Fact]
    public void Parse_Should_RejectAnEmptyRegistry()
    {
        ShouldFail("stacks: []").ShouldContain("at least one stack");
    }

    [Fact]
    public void Parse_Should_RejectAStackWithNoAzureCoordinates()
    {
        const string yaml = """
            stacks:
              - id: dsconnect-dev
                mappingFile: present.mapping.yaml
            """;

        ShouldFail(yaml).ShouldContain("stacks[0].azure is required");
    }

    [Fact]
    public void Parse_Should_RejectAMissingMappingFile()
    {
        var yaml = ValidYaml.Replace("present.mapping.yaml", "absent.mapping.yaml");

        // An entry whose mapping is absent would resolve no action at dispatch time, so it fails at
        // startup rather than as a per-request surprise.
        ShouldFail(yaml).ShouldContain("does not exist");
    }

    [Fact]
    public void Parse_Should_RejectANonAbsoluteQueueServiceUri()
    {
        var yaml = ValidYaml.Replace("https://stdev.queue.core.windows.net", "stdev-queue");

        ShouldFail(yaml).ShouldContain("is not an absolute URI");
    }

    [Fact]
    public void Parse_Should_RejectDuplicateStackIdsRegardlessOfCase()
    {
        var yaml = string.Concat(
            ValidYaml,
            """

              - id: DSCONNECT-DEV
                mappingFile: present.mapping.yaml
                azure:
                  subscriptionId: "sub-2"
                  resourceGroup: rg-other
                  queueServiceUri: https://stother.queue.core.windows.net
                  queueName: ai-jobs
                  jobName: ca-other-agent
                  logAnalyticsWorkspaceId: "workspace-2"
                vendor:
                  name: GitHub
                  repo: acme/widgets
                  readCredentialSecretName: Portal--s--Read
            """
        );

        // Ids key URLs and audit events -- two stacks sharing one would leave an audit trail
        // ambiguous about which client was touched.
        ShouldFail(yaml).ShouldContain("duplicate stack id");
    }

    [Fact]
    public void Parse_Should_ReportEveryViolationInOnePass()
    {
        const string yaml = """
            stacks:
              - mappingFile: absent.mapping.yaml
                azure:
                  resourceGroup: rg-x
                  queueServiceUri: "not a uri"
                  jobName: job-x
            """;

        // A misconfigured deploy must see the full list at once, not one error per restart.
        var message = ShouldFail(yaml);

        message.ShouldContain("stacks[0].id is required");
        message.ShouldContain("stacks[0].azure.subscriptionId is required");
        message.ShouldContain("stacks[0].azure.queueName is required");
        message.ShouldContain("stacks[0].azure.logAnalyticsWorkspaceId is required");
        message.ShouldContain("is not an absolute URI");
        message.ShouldContain("does not exist");
    }

    [Fact]
    public void LoadFromFile_Should_RejectAMissingRegistryFile()
    {
        var absent = Path.Combine(directory, "no-such-registry.yaml");

        Should
            .Throw<InvalidOperationException>(() => StackRegistryLoader.LoadFromFile(absent))
            .Message.ShouldContain("not found");
    }
}
