using Orchestrator.Modules.Backlog.Contracts;
using Orchestrator.Modules.Dispatch.Contracts;
using Shouldly;

namespace Orchestrator.Portal.UnitTests;

/// <summary>
/// The queue's wire format now has two writers — the receiver's webhook path and the portal's
/// manual-dispatch verb — and one reader. These pin the properties that would make those disagree, because
/// the failure mode is a message the agent cannot deserialize: discovered in production, on the dispatch
/// path, as a poison message rather than as an error anyone can read.
/// </summary>
public class QueueMessage_Should_Constraint
{
    static readonly CanonicalMessage Message = new(
        Vendor.Jira,
        Project: "DSC",
        Repo: "",
        ItemId: "DSC-42",
        State: "Ready for Proposal",
        Action: "/ds:propose",
        Runtime: "Claude",
        OnFailureState: "Blocked",
        CodeHost: Vendor.GitHub,
        CodeRepo: "acme/widgets"
    );

    [Fact]
    public void Serialize_Should_RoundTripEveryField()
    {
        var restored = QueueMessage.Deserialize(QueueMessage.Serialize(Message));

        restored.ShouldBe(Message);
    }

    [Fact]
    public void Serialize_Should_WriteEnumsAsNames()
    {
        var body = QueueMessage.Serialize(Message);

        // Names, not ordinals. The wire form has to survive a vendor being inserted into the middle of the
        // enum -- with ordinals, adding one would silently re-point every message already on a queue.
        body.ShouldContain("\"Jira\"");
        body.ShouldContain("\"GitHub\"");
        body.ShouldNotContain("\"Vendor\":3");
    }

    [Fact]
    public void Deserialize_Should_ReadWhatTheReceiverWould()
    {
        // The same shape AzureStorageWorkItemQueue produces, since both now go through this type. If this
        // ever needs updating by hand, the two writers have diverged.
        const string body = """
            {"Vendor":"GitHub","Project":null,"Repo":"acme/widgets","ItemId":"42",
             "State":"status:ready-for-proposal","Action":"/ds:propose","Runtime":"Claude",
             "OnFailureState":"status:blocked","CodeHost":"GitHub","CodeRepo":"acme/widgets"}
            """;

        var message = QueueMessage.Deserialize(body);

        message.ShouldNotBeNull();
        message.Vendor.ShouldBe(Vendor.GitHub);
        message.CodeHost.ShouldBe(Vendor.GitHub);
        message.ItemId.ShouldBe("42");
    }
}
