using Orchestrator.Modules.Backlog.Contracts;
using Orchestrator.Modules.Dispatch;
using Orchestrator.Modules.Dispatch.Contracts;
using Shouldly;

namespace Orchestrator.Modules.Dispatch.UnitTests;

public class MappingLoader_Should_Constraint
{
    const string ValidYaml = """
        targets:
          - vendor: GitHub
            repo: "acme/widgets"
            states:
              "status:ready-for-proposal":
                action: /ds:propose
                runtime: Claude
                onFailureState: "status:blocked"
        """;

    [Fact]
    public void Parse_Should_ResolveAKnownTargetAndState()
    {
        var config = MappingLoader.Parse(ValidYaml);
        var provider = new MappingProvider(config);

        var resolved = provider.TryResolve(
            Vendor.GitHub,
            project: null,
            repo: "acme/widgets",
            nativeState: "status:ready-for-proposal",
            out var mapping
        );

        resolved.ShouldBeTrue();
        mapping.ShouldNotBeNull();
        mapping.State.Action.ShouldBe("/ds:propose");
        mapping.State.Runtime.ShouldBe(AgentRuntime.Claude);
        mapping.State.OnFailureState.ShouldBe("status:blocked");
        // A target that declares neither codeHost nor codeRepo resolves to its own tracker/repo, so
        // every mapping written before those fields existed dispatches exactly as it did before.
        mapping.Target.CodeHost.ShouldBe(Vendor.GitHub);
        mapping.Target.CodeRepo.ShouldBe("acme/widgets");
    }

    [Fact]
    public void Parse_Should_BeRepoMatchCaseInsensitive()
    {
        var config = MappingLoader.Parse(ValidYaml);
        var provider = new MappingProvider(config);

        provider
            .TryResolve(Vendor.GitHub, null, "ACME/WIDGETS", "status:ready-for-proposal", out _)
            .ShouldBeTrue();
    }

    [Fact]
    public void Parse_Should_NotResolveAnUnmappedState()
    {
        var config = MappingLoader.Parse(ValidYaml);
        var provider = new MappingProvider(config);

        provider
            .TryResolve(
                Vendor.GitHub,
                null,
                "acme/widgets",
                "status:some-other-state",
                out var mapping
            )
            .ShouldBeFalse();
        mapping.ShouldBeNull();
    }

    [Fact]
    public void Parse_Should_NotResolveAnUnknownTarget()
    {
        var config = MappingLoader.Parse(ValidYaml);
        var provider = new MappingProvider(config);

        provider
            .TryResolve(
                Vendor.GitHub,
                null,
                "someone-else/repo",
                "status:ready-for-proposal",
                out _
            )
            .ShouldBeFalse();
    }

    [Fact]
    public void Parse_Should_ThrowOnEmptyTargets()
    {
        var act = () => MappingLoader.Parse("targets: []");

        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("at least one target");
    }

    [Fact]
    public void Parse_Should_ThrowWhenActionIsMissing()
    {
        const string yaml = """
            targets:
              - vendor: GitHub
                repo: "acme/widgets"
                states:
                  "status:ready-for-proposal":
                    runtime: Claude
                    onFailureState: "status:blocked"
            """;

        var act = () => MappingLoader.Parse(yaml);

        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("action is required");
    }

    [Fact]
    public void Parse_Should_ThrowOnUnknownVendor()
    {
        const string yaml = """
            targets:
              - vendor: Bitbucket
                repo: "acme/widgets"
                states:
                  "status:ready-for-proposal":
                    action: /ds:propose
                    runtime: Claude
                    onFailureState: "status:blocked"
            """;

        var act = () => MappingLoader.Parse(yaml);

        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("not a known vendor");
    }

    [Fact]
    public void Parse_Should_ThrowOnUnknownRuntime()
    {
        const string yaml = """
            targets:
              - vendor: GitHub
                repo: "acme/widgets"
                states:
                  "status:ready-for-proposal":
                    action: /ds:propose
                    runtime: Cursor
                    onFailureState: "status:blocked"
            """;

        var act = () => MappingLoader.Parse(yaml);

        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("not a known runtime");
    }

    const string SplitHostYaml = """
        targets:
          - vendor: Jira
            project: "DSC"
            codeHost: GitHub
            codeRepo: "acme/widgets"
            states:
              "Ready for Proposal":
                action: /ds:propose
                runtime: Claude
                onFailureState: "Blocked"
        """;

    [Fact]
    public void Parse_Should_ResolveANonRepoAnchoredTargetByProject()
    {
        var config = MappingLoader.Parse(SplitHostYaml);
        var provider = new MappingProvider(config);

        // The event carries no repo at all for a tracker whose items are project-scoped.
        var resolved = provider.TryResolve(
            Vendor.Jira,
            project: "DSC",
            repo: "",
            nativeState: "Ready for Proposal",
            out var mapping
        );

        resolved.ShouldBeTrue();
        mapping.ShouldNotBeNull();
        mapping.Target.CodeHost.ShouldBe(Vendor.GitHub);
        mapping.Target.CodeRepo.ShouldBe("acme/widgets");
        mapping.Target.Repo.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_Should_NotMatchANonRepoAnchoredTargetOnAnotherProject()
    {
        var config = MappingLoader.Parse(SplitHostYaml);
        var provider = new MappingProvider(config);

        provider.TryResolve(Vendor.Jira, "OTHER", "", "Ready for Proposal", out _).ShouldBeFalse();
    }

    [Fact]
    public void Parse_Should_NotMatchANonRepoAnchoredTargetByItsCodeRepo()
    {
        var config = MappingLoader.Parse(SplitHostYaml);
        var provider = new MappingProvider(config);

        // codeRepo is a coordinate on a different vendor entirely; it must never be used to match an
        // incoming tracker event, or a GitHub event could resolve a Jira target.
        provider
            .TryResolve(Vendor.Jira, null, "acme/widgets", "Ready for Proposal", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void Parse_Should_ThrowWhenNeitherRepoNorProjectIsDeclared()
    {
        const string yaml = """
            targets:
              - vendor: GitHub
                states:
                  "status:ready-for-proposal":
                    action: /ds:propose
                    runtime: Claude
                    onFailureState: "status:blocked"
            """;

        var act = () => MappingLoader.Parse(yaml);

        Should
            .Throw<InvalidOperationException>(act)
            .Message.ShouldContain("at least one of repo/project");
    }

    [Fact]
    public void Parse_Should_ThrowWhenThereIsNoRepositoryToClone()
    {
        // project-only, and no codeRepo to fall back on: nothing identifies a repository, which is a
        // configuration error rather than a run that fails after the agent has already started.
        const string yaml = """
            targets:
              - vendor: Jira
                project: "DSC"
                states:
                  "Ready for Proposal":
                    action: /ds:propose
                    runtime: Claude
                    onFailureState: "Blocked"
            """;

        var act = () => MappingLoader.Parse(yaml);

        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("nothing to clone");
    }

    [Fact]
    public void Parse_Should_ThrowOnUnknownCodeHost()
    {
        const string yaml = """
            targets:
              - vendor: GitHub
                repo: "acme/widgets"
                codeHost: Perforce
                states:
                  "status:ready-for-proposal":
                    action: /ds:propose
                    runtime: Claude
                    onFailureState: "status:blocked"
            """;

        var act = () => MappingLoader.Parse(yaml);

        Should
            .Throw<InvalidOperationException>(act)
            .Message.ShouldContain("codeHost 'Perforce' is not a known vendor");
    }

    [Fact]
    public void Parse_Should_LoadTheShippedMappingFileUnchanged()
    {
        // The regression net for "existing mappings need no edit" (jira-adapter task 2.4): the file
        // the Receiver actually ships, parsed as-is, with no codeHost/codeRepo anywhere in it.
        var src = new DirectoryInfo(AppContext.BaseDirectory);
        while (src is not null && !File.Exists(Path.Combine(src.FullName, "orchestrator.slnx")))
        {
            src = src.Parent;
        }

        src.ShouldNotBeNull("could not locate orchestrator.slnx above the test output directory");
        var path = Path.Combine(src.FullName, "root", "Orchestrator.Receiver", "mapping.yaml");

        File.Exists(path).ShouldBeTrue($"expected the shipped mapping at {path}");

        var config = MappingLoader.LoadFromFile(path);

        config.Targets.ShouldNotBeEmpty();
        foreach (var target in config.Targets)
        {
            target.CodeHost.ShouldBe(target.Vendor);
            target.CodeRepo.ShouldBe(target.Repo);
        }
    }
}
