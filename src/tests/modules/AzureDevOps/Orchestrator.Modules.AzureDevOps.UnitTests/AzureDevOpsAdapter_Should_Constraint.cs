using System.Text;
using Microsoft.Extensions.Options;
using Orchestrator.Modules.AzureDevOps;
using Orchestrator.Modules.Backlog.Contracts;
using Shouldly;

namespace Orchestrator.Modules.AzureDevOps.UnitTests;

public class AzureDevOpsAdapter_Should_Constraint
{
    static AzureDevOpsAdapter BuildAdapter(
        string webhookUsername = "aca-hook",
        string webhookPassword = "hook-secret",
        string botIdentity = "ai-loop-bot@example.com"
    ) =>
        new(
            new HttpClient(),
            Options.Create(
                new AzureDevOpsOptions
                {
                    Organization = "contoso",
                    BotIdentity = botIdentity,
                    PersonalAccessToken = "pat",
                    WebhookUsername = webhookUsername,
                    WebhookPassword = webhookPassword,
                }
            )
        );

    static ReadOnlyMemory<byte> Body(string json) => Encoding.UTF8.GetBytes(json);

    static string BasicAuth(string user, string password) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));

    const string StateChangedPayload = """
        {
          "eventType": "workitem.updated",
          "resource": {
            "id": 5,
            "workItemId": 5,
            "revisedBy": { "uniqueName": "someone@example.com", "displayName": "Someone" },
            "fields": {
              "System.State": { "oldValue": "New", "newValue": "status:ready-for-proposal" }
            },
            "revision": {
              "fields": {
                "System.TeamProject": "Widgets",
                "System.State": "status:ready-for-proposal"
              }
            }
          }
        }
        """;

    [Fact]
    public void VerifyWebhook_Should_AcceptTheConfiguredBasicAuthCredentials()
    {
        var adapter = BuildAdapter(webhookUsername: "aca-hook", webhookPassword: "hook-secret");
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = BasicAuth("aca-hook", "hook-secret"),
        };

        adapter.VerifyWebhook(Body(StateChangedPayload), headers).ShouldBeTrue();
    }

    [Fact]
    public void VerifyWebhook_Should_RejectWrongCredentials()
    {
        var adapter = BuildAdapter(webhookUsername: "aca-hook", webhookPassword: "hook-secret");
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = BasicAuth("aca-hook", "wrong-secret"),
        };

        adapter.VerifyWebhook(Body(StateChangedPayload), headers).ShouldBeFalse();
    }

    [Fact]
    public void VerifyWebhook_Should_RejectAMissingAuthorizationHeader()
    {
        var adapter = BuildAdapter();

        adapter
            .VerifyWebhook(Body(StateChangedPayload), new Dictionary<string, string>())
            .ShouldBeFalse();
    }

    [Fact]
    public void ParseEvent_Should_ExtractTheNewStateProjectAndActor()
    {
        var adapter = BuildAdapter();

        var canonicalEvent = adapter.ParseEvent(
            Body(StateChangedPayload),
            new Dictionary<string, string>()
        );

        canonicalEvent.ShouldNotBeNull();
        canonicalEvent.Vendor.ShouldBe(Vendor.AzureDevOps);
        canonicalEvent.Project.ShouldBe("contoso");
        canonicalEvent.Repo.ShouldBe("Widgets");
        canonicalEvent.ItemId.ShouldBe("5");
        canonicalEvent.NativeState.ShouldBe("status:ready-for-proposal");
        canonicalEvent.Actor.ShouldBe("someone@example.com");
    }

    [Fact]
    public void ParseEvent_Should_ReturnNullForANonWorkItemUpdatedEvent()
    {
        const string payload = """{ "eventType": "workitem.created", "resource": {} }""";
        var adapter = BuildAdapter();

        adapter.ParseEvent(Body(payload), new Dictionary<string, string>()).ShouldBeNull();
    }

    [Fact]
    public void ParseEvent_Should_ReturnNullWhenTheUpdateDidNotTouchState()
    {
        const string payload = """
            {
              "eventType": "workitem.updated",
              "resource": {
                "id": 5,
                "fields": {
                  "System.Title": { "oldValue": "Old title", "newValue": "New title" }
                },
                "revision": { "fields": { "System.TeamProject": "Widgets" } }
              }
            }
            """;
        var adapter = BuildAdapter();

        adapter.ParseEvent(Body(payload), new Dictionary<string, string>()).ShouldBeNull();
    }

    [Fact]
    public void IsAutomationActor_Should_BeCaseInsensitive()
    {
        var adapter = BuildAdapter(botIdentity: "ai-loop-bot@example.com");

        adapter.IsAutomationActor("AI-LOOP-BOT@EXAMPLE.COM").ShouldBeTrue();
        adapter.IsAutomationActor("someone-else@example.com").ShouldBeFalse();
    }
}
