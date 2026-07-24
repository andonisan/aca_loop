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
        mapping.Action.ShouldBe("/ds:propose");
        mapping.Runtime.ShouldBe(AgentRuntime.Claude);
        mapping.OnFailureState.ShouldBe("status:blocked");
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
}
