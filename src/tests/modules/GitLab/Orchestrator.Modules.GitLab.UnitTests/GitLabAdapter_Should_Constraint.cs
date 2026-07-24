using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Orchestrator.Modules.Backlog.Contracts;
using Orchestrator.Modules.GitLab;
using Shouldly;

namespace Orchestrator.Modules.GitLab.UnitTests;

public class GitLabAdapter_Should_Constraint
{
    static GitLabAdapter BuildAdapter(string webhookSecret = "shared-secret", string botUsername = "ai-loop-bot") =>
        new(
            new HttpClient(),
            Options.Create(
                new GitLabOptions
                {
                    BotUsername = botUsername,
                    AccessToken = "token",
                    WebhookSecret = webhookSecret,
                }
            )
        );

    static ReadOnlyMemory<byte> Body(string json) => Encoding.UTF8.GetBytes(json);

    const string LabelAddedPayload = """
        {
          "object_kind": "issue",
          "user": { "username": "someone" },
          "project": { "path_with_namespace": "acme/widgets" },
          "object_attributes": { "iid": 42, "action": "update" },
          "changes": {
            "labels": {
              "previous": [{"title": "status:backlog"}],
              "current": [{"title": "status:backlog"}, {"title": "status:ready-for-proposal"}]
            }
          }
        }
        """;

    [Fact]
    public void VerifyWebhook_Should_AcceptTheConfiguredSecret()
    {
        var adapter = BuildAdapter(webhookSecret: "shared-secret");
        var headers = new Dictionary<string, string> { ["X-Gitlab-Token"] = "shared-secret" };

        adapter.VerifyWebhook(Body(LabelAddedPayload), headers).ShouldBeTrue();
    }

    [Fact]
    public void VerifyWebhook_Should_RejectAWrongSecret()
    {
        var adapter = BuildAdapter(webhookSecret: "shared-secret");
        var headers = new Dictionary<string, string> { ["X-Gitlab-Token"] = "wrong" };

        adapter.VerifyWebhook(Body(LabelAddedPayload), headers).ShouldBeFalse();
    }

    [Fact]
    public void VerifyWebhook_Should_RejectAMissingTokenHeader()
    {
        var adapter = BuildAdapter();

        adapter.VerifyWebhook(Body(LabelAddedPayload), new Dictionary<string, string>()).ShouldBeFalse();
    }

    [Fact]
    public void ParseEvent_Should_DetectAnAddedLabelAsTheNativeState()
    {
        var adapter = BuildAdapter();
        var headers = new Dictionary<string, string> { ["X-Gitlab-Event"] = "Issue Hook" };

        var canonicalEvent = adapter.ParseEvent(Body(LabelAddedPayload), headers);

        canonicalEvent.ShouldNotBeNull();
        canonicalEvent.Vendor.ShouldBe(Vendor.GitLab);
        canonicalEvent.Repo.ShouldBe("acme/widgets");
        canonicalEvent.ItemId.ShouldBe("42");
        canonicalEvent.NativeState.ShouldBe("status:ready-for-proposal");
        canonicalEvent.Actor.ShouldBe("someone");
    }

    [Fact]
    public void ParseEvent_Should_ReturnNullForANonIssueHookEvent()
    {
        var adapter = BuildAdapter();
        var headers = new Dictionary<string, string> { ["X-Gitlab-Event"] = "Push Hook" };

        adapter.ParseEvent(Body(LabelAddedPayload), headers).ShouldBeNull();
    }

    [Fact]
    public void ParseEvent_Should_ReturnNullWhenNoLabelWasAdded()
    {
        const string payload = """
            {
              "user": { "username": "someone" },
              "project": { "path_with_namespace": "acme/widgets" },
              "object_attributes": { "iid": 42, "action": "update" },
              "changes": {
                "labels": {
                  "previous": [{"title": "status:backlog"}, {"title": "status:ready-for-proposal"}],
                  "current": [{"title": "status:backlog"}]
                }
              }
            }
            """;
        var adapter = BuildAdapter();
        var headers = new Dictionary<string, string> { ["X-Gitlab-Event"] = "Issue Hook" };

        adapter.ParseEvent(Body(payload), headers).ShouldBeNull();
    }

    [Fact]
    public void ParseEvent_Should_ReturnNullWhenTheUpdateHasNoLabelChanges()
    {
        const string payload = """
            {
              "user": { "username": "someone" },
              "project": { "path_with_namespace": "acme/widgets" },
              "object_attributes": { "iid": 42, "action": "update" },
              "changes": {}
            }
            """;
        var adapter = BuildAdapter();
        var headers = new Dictionary<string, string> { ["X-Gitlab-Event"] = "Issue Hook" };

        adapter.ParseEvent(Body(payload), headers).ShouldBeNull();
    }

    [Fact]
    public void IsAutomationActor_Should_BeCaseInsensitive()
    {
        var adapter = BuildAdapter(botUsername: "ai-loop-bot");

        adapter.IsAutomationActor("AI-LOOP-BOT").ShouldBeTrue();
        adapter.IsAutomationActor("someone-else").ShouldBeFalse();
    }
}
