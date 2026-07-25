using Orchestrator.Modules.Backlog.Contracts;
using Orchestrator.Modules.ConfigStore.Contracts;
using Orchestrator.Modules.ConfigStore.Features.Stacks;
using Orchestrator.Modules.Dispatch.Contracts;
using Shouldly;

namespace Orchestrator.Modules.ConfigStore.UnitTests;

/// <summary>
/// Rows and files are two sources for one validated contract (design D5). These pin that the store runs the
/// *same* validator rather than a second implementation of "what a valid mapping is" — the copy that would
/// be free to drift on exactly the rules deciding which states spend agent money.
/// <para>
/// No container needed: this is the conversion and the shared validation, not the storage.
/// </para>
/// </summary>
public class StoredMapping_Should_Constraint
{
    static StoredStack Stack(params StoredState[] states) =>
        new(
            "dsconnect-dev",
            "ds-connect (dev)",
            new StoredAzureCoordinates(
                "sub",
                "rg",
                new Uri("https://acct.queue.core.windows.net"),
                "ai-jobs",
                "agent-job",
                "ws"
            ),
            new StoredVendorCoordinates(
                Vendor.GitHub,
                null,
                "acme/widgets",
                "Portal--Read",
                new Uri("https://api.github.com"),
                null
            ),
            states
        );

    static StoredState State(string native, string action = "/ds:propose") =>
        new(
            native,
            action,
            AgentRuntime.Claude,
            null,
            "status:blocked",
            Vendor.GitHub,
            "acme/widgets"
        );

    [Fact]
    public void ToMappingConfig_Should_ProduceAResolvableTarget()
    {
        var config = StoredMapping.ToMappingConfig(Stack(State("status:ready-for-proposal")));

        var target = config.Targets.ShouldHaveSingleItem();
        target.Vendor.ShouldBe(Vendor.GitHub);
        target.Repo.ShouldBe("acme/widgets");
        target.CodeHost.ShouldBe(Vendor.GitHub);
        target.States.ShouldContainKey("status:ready-for-proposal");
    }

    [Fact]
    public void ToMappingConfig_Should_ResolveThroughTheSameProviderTheReceiverUses()
    {
        var config = StoredMapping.ToMappingConfig(
            Stack(
                State("status:ready-for-proposal"),
                State("status:ready-for-implementation", "/ds:implement")
            )
        );

        // The end-to-end property that matters: a stack read from rows resolves identically to one read
        // from a file, because both arrive as the same validated MappingConfig.
        new MappingProvider(config)
            .TryResolve(
                Vendor.GitHub,
                null,
                "acme/widgets",
                "status:ready-for-implementation",
                out var resolved
            )
            .ShouldBeTrue();

        resolved!.State.Action.ShouldBe("/ds:implement");
    }

    [Fact]
    public void ToMappingConfig_Should_RejectAnInvalidStoredMappingRatherThanTrustIt()
    {
        // A row written by a path that never went through the portal's validation -- which design D3 says
        // exists. The reader must not assume rows are well-formed just because the portal would not have
        // written them badly.
        var badState = new StoredState(
            "status:ready-for-proposal",
            Action: "",
            AgentRuntime.Claude,
            null,
            "status:blocked",
            Vendor.GitHub,
            "acme/widgets"
        );

        var act = () => StoredMapping.ToMappingConfig(Stack(badState));

        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("action is required");
    }

    [Fact]
    public void ToMappingConfig_Should_RejectAStackWithNoStates()
    {
        // Same rule the file format has: a target declaring no states dispatches nothing, so it is a
        // configuration mistake rather than a valid empty case.
        var act = () => StoredMapping.ToMappingConfig(Stack());

        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("at least one state");
    }
}
