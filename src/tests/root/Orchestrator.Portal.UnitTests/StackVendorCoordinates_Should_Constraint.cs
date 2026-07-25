using Orchestrator.Modules.Backlog.Contracts;
using Orchestrator.Portal.Registry;
using Shouldly;

namespace Orchestrator.Portal.UnitTests;

/// <summary>
/// Vendor coordinates are required, not optional: every vendor read the portal makes needs them, and a
/// stack that cannot be read is not one this portal can show (design D9). These pin the validation rather
/// than leaving a half-configured stack to fail at render time.
/// </summary>
public class StackVendorCoordinates_Should_Constraint
{
    const string AzureBlock = """
            azure:
              subscriptionId: sub
              resourceGroup: rg
              queueServiceUri: https://acct.queue.core.windows.net
              queueName: ai-jobs
              jobName: agent-job
              logAnalyticsWorkspaceId: workspace-1
        """;

    static string Registry(string vendorBlock) =>
        $"""
            stacks:
              - id: s
                displayName: S
                mappingFile: m.yaml
            {AzureBlock}
            {vendorBlock}
            """;

    static string ParseFailure(string yaml, string mappingDirectory)
    {
        var act = () => StackRegistryLoader.Parse(yaml, mappingDirectory);
        return Should.Throw<InvalidOperationException>(act).Message;
    }

    static string WithMappingFile()
    {
        var directory = Directory.CreateTempSubdirectory("registry-vendor-").FullName;
        File.WriteAllText(Path.Combine(directory, "m.yaml"), "targets: []");
        return directory;
    }

    [Fact]
    public void Parse_Should_RequireAVendorBlock()
    {
        ParseFailure(Registry(""), WithMappingFile()).ShouldContain("stacks[0].vendor is required");
    }

    [Fact]
    public void Parse_Should_RejectAnUnknownVendorName()
    {
        var yaml = Registry(
            """
                vendor:
                  name: Trello
                  repo: acme/widgets
                  readCredentialSecretName: Portal--s--Read
            """
        );

        ParseFailure(yaml, WithMappingFile())
            .ShouldContain("stacks[0].vendor.name 'Trello' is not a known vendor");
    }

    [Fact]
    public void Parse_Should_RequireAReadScopedCredentialName()
    {
        // The whole point of D9: without this the portal has no credential of its own to read with, and
        // the tempting fix is reaching for the agent's write-capable one.
        var yaml = Registry(
            """
                vendor:
                  name: GitHub
                  repo: acme/widgets
            """
        );

        ParseFailure(yaml, WithMappingFile())
            .ShouldContain("stacks[0].vendor.readCredentialSecretName is required");
    }

    [Fact]
    public void Parse_Should_RequireAtLeastOneOfRepoOrProject()
    {
        var yaml = Registry(
            """
                vendor:
                  name: Jira
                  readCredentialSecretName: Portal--s--Read
            """
        );

        ParseFailure(yaml, WithMappingFile())
            .ShouldContain("stacks[0].vendor must declare at least one of repo/project");
    }

    [Fact]
    public void Parse_Should_AcceptANonRepoAnchoredTrackerWithOnlyAProject()
    {
        // A Jira project has no repo of its own -- the same rule the mapping loader applies to a target.
        var yaml = Registry(
            """
                vendor:
                  name: Jira
                  project: DSC
                  readCredentialSecretName: Portal--s--Read
            """
        );

        var registry = StackRegistryLoader.Parse(yaml, WithMappingFile());

        var vendor = registry.Stacks.ShouldHaveSingleItem().Vendor;
        vendor.Vendor.ShouldBe(Vendor.Jira);
        vendor.Project.ShouldBe("DSC");
        vendor.Repo.ShouldBeEmpty();
        vendor.ReadCredentialSecretName.ShouldBe("Portal--s--Read");
    }
}
