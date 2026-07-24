using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Orchestrator.Modules.Backlog.Contracts;
using Shouldly;

namespace Orchestrator.Modules.Jira.UnitTests;

public class JiraAdapter_Should_Constraint
{
    const string Secret = "shared-secret";
    const string BotAccountId = "5b10a2844c20165700ede21g";

    static JiraAdapter BuildAdapter(
        RecordingHandler? handler = null,
        JiraDeployment deployment = JiraDeployment.Cloud
    ) =>
        new(
            new HttpClient(handler ?? new RecordingHandler(RecordingHandler.Empty()))
            {
                BaseAddress = new Uri("https://acme.atlassian.net"),
            },
            Options.Create(
                new JiraOptions
                {
                    BaseUrl = "https://acme.atlassian.net",
                    Deployment = deployment,
                    AccountEmail = "bot@acme.com",
                    ApiToken = "api-token",
                    WebhookSecret = Secret,
                    BotAccountId = BotAccountId,
                    // Zero backoff: these tests prove the retry policy, not the clock.
                    RetryBaseDelayMilliseconds = 0,
                    MaxRetries = 2,
                }
            )
        );

    static ReadOnlyMemory<byte> Body(string json) => Encoding.UTF8.GetBytes(json);

    /// <summary>Jira carries the event type in the body, not a header, so parsing needs none.</summary>
    static readonly Dictionary<string, string> NoHeaders = [];

    static Dictionary<string, string> SignedHeaders(string json, string method = "sha256")
    {
        using HMAC hmac =
            method == "sha1"
                ? new HMACSHA1(Encoding.UTF8.GetBytes(Secret))
                : new HMACSHA256(Encoding.UTF8.GetBytes(Secret));

        var signature = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(json)));

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Hub-Signature"] = $"{method}={signature}",
        };
    }

    const string StatusChangedPayload = """
        {
          "webhookEvent": "jira:issue_updated",
          "user": { "accountId": "someone-else", "displayName": "A Human" },
          "issue": {
            "id": "10731",
            "key": "DSC-42",
            "fields": { "project": { "key": "DSC" }, "status": { "name": "Ready for Proposal" } }
          },
          "changelog": {
            "items": [
              { "field": "assignee", "toString": "Someone" },
              { "field": "status", "from": "1", "fromString": "To Do", "to": "3", "toString": "Ready for Proposal" }
            ]
          }
        }
        """;

    // ---- webhook authenticity (design.md D2) ----

    [Fact]
    public void VerifyWebhook_Should_AcceptAValidSha256Signature()
    {
        BuildAdapter()
            .VerifyWebhook(Body(StatusChangedPayload), SignedHeaders(StatusChangedPayload))
            .ShouldBeTrue();
    }

    [Fact]
    public void VerifyWebhook_Should_RejectAWrongSignature()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Hub-Signature"] = "sha256=" + new string('0', 64),
        };

        BuildAdapter().VerifyWebhook(Body(StatusChangedPayload), headers).ShouldBeFalse();
    }

    [Fact]
    public void VerifyWebhook_Should_RejectAMissingSignatureHeader()
    {
        // Atlassian treats the secret as optional at registration; this adapter must not treat an
        // absent signature as "verification not configured".
        BuildAdapter()
            .VerifyWebhook(Body(StatusChangedPayload), new Dictionary<string, string>())
            .ShouldBeFalse();
    }

    [Fact]
    public void VerifyWebhook_Should_RejectAnUnsupportedHmacMethod()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Hub-Signature"] = "md5=whatever",
        };

        BuildAdapter().VerifyWebhook(Body(StatusChangedPayload), headers).ShouldBeFalse();
    }

    [Fact]
    public void VerifyWebhook_Should_AcceptTheDocumentedMethodPrefixOtherThanSha256()
    {
        // The method is parsed from the header rather than assumed, because Atlassian documents that
        // it may change.
        BuildAdapter()
            .VerifyWebhook(Body(StatusChangedPayload), SignedHeaders(StatusChangedPayload, "sha1"))
            .ShouldBeTrue();
    }

    // ---- event normalization (design.md D4, D7) ----

    [Fact]
    public void ParseEvent_Should_NormalizeAStatusChange()
    {
        var result = BuildAdapter().ParseEvent(Body(StatusChangedPayload), NoHeaders);

        result.ShouldNotBeNull();
        result.Vendor.ShouldBe(Vendor.Jira);
        result.Project.ShouldBe("DSC");
        result.NativeState.ShouldBe("Ready for Proposal");
        result.Actor.ShouldBe("someone-else");
        // The issue key, never the numeric id (10731 above) -- legible in logs and the portal, and
        // accepted everywhere Jira's REST takes issueIdOrKey.
        result.ItemId.ShouldBe("DSC-42");
        // A Jira issue belongs to a project, not a repository.
        result.Repo.ShouldBeEmpty();
    }

    [Fact]
    public void ParseEvent_Should_DropAnUpdateThatDidNotTouchStatus()
    {
        const string payload = """
            {
              "webhookEvent": "jira:issue_updated",
              "user": { "accountId": "someone-else" },
              "issue": { "key": "DSC-42", "fields": { "project": { "key": "DSC" } } },
              "changelog": { "items": [ { "field": "summary", "toString": "A new summary" } ] }
            }
            """;

        BuildAdapter().ParseEvent(Body(payload), NoHeaders).ShouldBeNull();
    }

    [Fact]
    public void ParseEvent_Should_DropANonUpdateEvent()
    {
        const string payload = """
            {
              "webhookEvent": "jira:issue_created",
              "issue": { "key": "DSC-42", "fields": { "project": { "key": "DSC" } } }
            }
            """;

        BuildAdapter().ParseEvent(Body(payload), NoHeaders).ShouldBeNull();
    }

    [Fact]
    public void ParseEvent_Should_DropAnUpdateWithNoChangelog()
    {
        const string payload = """
            {
              "webhookEvent": "jira:issue_updated",
              "issue": { "key": "DSC-42", "fields": { "project": { "key": "DSC" } } }
            }
            """;

        BuildAdapter().ParseEvent(Body(payload), NoHeaders).ShouldBeNull();
    }

    [Fact]
    public void ParseEvent_Should_ReadTheDataCenterActorShape()
    {
        // Data Center has no account ids; the actor arrives as `name`.
        const string payload = """
            {
              "webhookEvent": "jira:issue_updated",
              "user": { "name": "dc-user", "displayName": "DC User" },
              "issue": { "key": "VY-1", "fields": { "project": { "key": "VY" } } },
              "changelog": { "items": [ { "field": "status", "toString": "Open" } ] }
            }
            """;

        BuildAdapter().ParseEvent(Body(payload), NoHeaders)!.Actor.ShouldBe("dc-user");
    }

    // ---- automation self-filtering ----

    [Fact]
    public void IsAutomationActor_Should_MatchTheConfiguredBotAccountId()
    {
        BuildAdapter().IsAutomationActor(BotAccountId).ShouldBeTrue();
    }

    [Fact]
    public void IsAutomationActor_Should_NotMatchAnotherAccountOrNull()
    {
        var adapter = BuildAdapter();

        adapter.IsAutomationActor("someone-else").ShouldBeFalse();
        adapter.IsAutomationActor(null).ShouldBeFalse();
    }

    // ---- live state read ----

    [Fact]
    public async Task ReadItemStateAsync_Should_ReturnTheLiveStatusName()
    {
        var handler = new RecordingHandler(
            RecordingHandler.Json("""{"fields":{"status":{"name":"In Progress"}}}""")
        );

        var state = await BuildAdapter(handler).ReadItemStateAsync(null, "", "DSC-42");

        state.ShouldBe("In Progress");
        handler
            .Requests[0]
            .RequestUri!.PathAndQuery.ShouldBe("/rest/api/3/issue/DSC-42?fields=status");
    }

    // ---- write-back through transition discovery (design.md D5) ----

    [Fact]
    public async Task TransitionStateAsync_Should_ExecuteTheTransitionWhoseTargetMatches()
    {
        var handler = new RecordingHandler(
            RecordingHandler.Json(
                """
                {"transitions":[
                  {"id":"11","to":{"name":"In Progress"}},
                  {"id":"21","to":{"name":"Blocked"}}
                ]}
                """
            ),
            RecordingHandler.Empty()
        );

        await BuildAdapter(handler).TransitionStateAsync(null, "", "DSC-42", "Blocked");

        handler.Requests.Count.ShouldBe(2);
        handler.Requests[0].Method.ShouldBe(HttpMethod.Get);
        handler.Requests[1].Method.ShouldBe(HttpMethod.Post);
        handler
            .Requests[1]
            .RequestUri!.PathAndQuery.ShouldBe("/rest/api/3/issue/DSC-42/transitions");
        // By id, matched from the discovered list -- status is never sent as a field.
        handler.Bodies[1].ShouldContain("\"id\":\"21\"");
        handler.Bodies[1].ShouldNotContain("status");
    }

    [Fact]
    public async Task TransitionStateAsync_Should_ThrowNamingWhatIsReachableWhenTheTargetIsNot()
    {
        // The failure state is not always reachable from the item's current status. Execution treats
        // this as context on an already-recorded failure, so the message has to say what *is* possible.
        var handler = new RecordingHandler(
            RecordingHandler.Json("""{"transitions":[{"id":"11","to":{"name":"In Progress"}}]}""")
        );

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            BuildAdapter(handler).TransitionStateAsync(null, "", "DSC-42", "Blocked")
        );

        exception.Message.ShouldContain("'Blocked'");
        exception.Message.ShouldContain("In Progress");
        // Nothing was written.
        handler.Requests.Count.ShouldBe(1);
    }

    // ---- comments, per deployment (design.md D11) ----

    [Fact]
    public async Task PostCommentAsync_Should_UseV3AndAtlassianDocumentFormatOnCloud()
    {
        var handler = new RecordingHandler(RecordingHandler.Empty());

        await BuildAdapter(handler, JiraDeployment.Cloud)
            .PostCommentAsync(null, "", "DSC-42", "first line\nsecond line");

        handler.Requests[0].RequestUri!.PathAndQuery.ShouldBe("/rest/api/3/issue/DSC-42/comment");
        handler.Bodies[0].ShouldContain("\"type\":\"doc\"");
        handler.Bodies[0].ShouldContain("\"paragraph\"");
        // One paragraph per line: ADF text nodes have no notion of a newline, so a multi-line log
        // tail would otherwise collapse onto a single line.
        handler.Bodies[0].ShouldContain("first line");
        handler.Bodies[0].ShouldContain("second line");
    }

    [Fact]
    public async Task PostCommentAsync_Should_UseV2AndPlainTextOnDataCenter()
    {
        var handler = new RecordingHandler(RecordingHandler.Empty());

        await BuildAdapter(handler, JiraDeployment.DataCenter)
            .PostCommentAsync(null, "", "VY-1", "plain body");

        // Data Center has no /rest/api/3 and no Atlassian Document Format at all.
        handler.Requests[0].RequestUri!.PathAndQuery.ShouldBe("/rest/api/2/issue/VY-1/comment");
        handler.Bodies[0].ShouldContain("\"body\":\"plain body\"");
        handler.Bodies[0].ShouldNotContain("\"type\":\"doc\"");
    }

    // ---- auth, per deployment (design.md D10/D11) ----

    [Fact]
    public async Task SendAsync_Should_UseBasicOnCloudAndBearerOnDataCenter()
    {
        var cloud = new RecordingHandler(
            RecordingHandler.Json("""{"fields":{"status":{"name":"Open"}}}""")
        );
        var dataCenter = new RecordingHandler(
            RecordingHandler.Json("""{"fields":{"status":{"name":"Open"}}}""")
        );

        await BuildAdapter(cloud, JiraDeployment.Cloud).ReadItemStateAsync(null, "", "DSC-42");
        await BuildAdapter(dataCenter, JiraDeployment.DataCenter)
            .ReadItemStateAsync(null, "", "VY-1");

        cloud.Requests[0].Headers.Authorization!.Scheme.ShouldBe("Basic");
        dataCenter.Requests[0].Headers.Authorization!.Scheme.ShouldBe("Bearer");
    }

    // ---- outbound resilience (design.md D12) ----

    [Fact]
    public async Task SendAsync_Should_RetryARateLimitedCallAndThenSucceed()
    {
        var handler = new RecordingHandler(
            RecordingHandler.RateLimited(retryAfterSeconds: 0),
            RecordingHandler.Json("""{"fields":{"status":{"name":"Done"}}}""")
        );

        var state = await BuildAdapter(handler).ReadItemStateAsync(null, "", "DSC-42");

        state.ShouldBe("Done");
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task SendAsync_Should_ThrowAfterExhaustingItsBoundedRetries()
    {
        var handler = new RecordingHandler(RecordingHandler.RateLimited());

        await Should.ThrowAsync<InvalidOperationException>(() =>
            BuildAdapter(handler).ReadItemStateAsync(null, "", "DSC-42")
        );

        // MaxRetries = 2 in these tests, so three attempts total and then it gives up rather than
        // holding the replica open indefinitely.
        handler.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task SendAsync_Should_NotRetryAClientError()
    {
        var handler = new RecordingHandler(
            RecordingHandler.Empty(System.Net.HttpStatusCode.NotFound)
        );

        await Should.ThrowAsync<InvalidOperationException>(() =>
            BuildAdapter(handler).ReadItemStateAsync(null, "", "NOPE-1")
        );

        // A 404 will not become a 200 by asking again.
        handler.Requests.Count.ShouldBe(1);
    }
}
