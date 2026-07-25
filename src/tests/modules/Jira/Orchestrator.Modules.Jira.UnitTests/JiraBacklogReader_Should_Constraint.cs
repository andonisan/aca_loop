using Microsoft.Extensions.Options;
using Orchestrator.Modules.Backlog.Contracts;
using Shouldly;

namespace Orchestrator.Modules.Jira.UnitTests;

/// <summary>
/// The gate listing for Jira. One JQL query covers every state, so the risk is not round-trip count — it is
/// the query string itself, which is assembled from operator-authored status names that routinely contain
/// spaces and punctuation.
/// </summary>
public class JiraBacklogReader_Should_Constraint
{
    static JiraBacklogReader Build(
        RecordingHandler handler,
        JiraDeployment deployment = JiraDeployment.Cloud
    ) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://acme.atlassian.net") },
            Options.Create(
                new JiraOptions
                {
                    BaseUrl = "https://acme.atlassian.net",
                    Deployment = deployment,
                    AccountEmail = "bot@acme.com",
                    ApiToken = "unused-here",
                    WebhookSecret = "s",
                    BotAccountId = "bot",
                }
            )
        )
        {
            ReadTokenFor = (_, _) => Task.FromResult("browse-only-token"),
        };

    const string OneIssue = """
        {
          "issues": [
            {
              "key": "DSC-42",
              "fields": {
                "summary": "Add the thing",
                "status": { "name": "Ready for Proposal" },
                "assignee": { "displayName": "A Human" },
                "updated": "2026-07-25T10:00:00.000+0000"
              }
            }
          ]
        }
        """;

    [Fact]
    public async Task ListItemsInStatesAsync_Should_AskForEveryStateInOneQuery()
    {
        var handler = new RecordingHandler(RecordingHandler.Json("""{"issues":[]}"""));

        await Build(handler)
            .ListItemsInStatesAsync("DSC", "", ["Ready for Proposal", "Code review"]);

        // One round trip regardless of state count -- the reason IBacklogReader takes a collection rather
        // than being called per state. Jira's rate limits are the tightest of the four vendors.
        handler.Requests.Count.ShouldBe(1);

        var query = Uri.UnescapeDataString(handler.Requests[0].RequestUri!.Query);
        query.ShouldContain("status IN (\"Ready for Proposal\", \"Code review\")");
        query.ShouldContain("project = \"DSC\"");
    }

    [Fact]
    public async Task ListItemsInStatesAsync_Should_QuoteStatusNamesContainingPunctuation()
    {
        var handler = new RecordingHandler(RecordingHandler.Json("""{"issues":[]}"""));

        // A real status name from the observed vocabulary catalog. Unquoted, JQL would fail to parse and
        // the gate view would render as an error rather than a list.
        await Build(handler).ListItemsInStatesAsync("VY", "", ["In Review / Deployment"]);

        Uri.UnescapeDataString(handler.Requests[0].RequestUri!.Query)
            .ShouldContain("\"In Review / Deployment\"");
    }

    [Fact]
    public async Task ListItemsInStatesAsync_Should_RefuseToQueryWithoutAProjectKey()
    {
        var handler = new RecordingHandler(RecordingHandler.Json("""{"issues":[]}"""));

        var items = await Build(handler).ListItemsInStatesAsync(null, "", ["Open"]);

        // Without a project to scope to, the JQL would list every issue the account can see -- across
        // every client on that instance. Refusing is the only safe answer.
        items.ShouldBeEmpty();
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListItemsInStatesAsync_Should_ProjectTheFieldsAViewNeeds()
    {
        var handler = new RecordingHandler(RecordingHandler.Json(OneIssue));

        var item = (
            await Build(handler).ListItemsInStatesAsync("DSC", "", ["Ready for Proposal"])
        ).ShouldHaveSingleItem();

        // The issue key, never the numeric id -- matching what the adapter uses and what a human quotes.
        item.ItemId.ShouldBe("DSC-42");
        item.Title.ShouldBe("Add the thing");
        item.NativeState.ShouldBe("Ready for Proposal");
        item.Assignee.ShouldBe("A Human");
        item.Url!.ToString().ShouldBe("https://acme.atlassian.net/browse/DSC-42");
    }

    [Fact]
    public async Task ListItemsInStatesAsync_Should_UseTheDeploymentsAuthScheme()
    {
        var cloud = new RecordingHandler(RecordingHandler.Json("""{"issues":[]}"""));
        var dataCenter = new RecordingHandler(RecordingHandler.Json("""{"issues":[]}"""));

        await Build(cloud, JiraDeployment.Cloud).ListItemsInStatesAsync("DSC", "", ["Open"]);
        await Build(dataCenter, JiraDeployment.DataCenter)
            .ListItemsInStatesAsync("DSC", "", ["Open"]);

        cloud.Requests[0].Headers.Authorization!.Scheme.ShouldBe("Basic");
        dataCenter.Requests[0].Headers.Authorization!.Scheme.ShouldBe("Bearer");
        // And the v2/v3 split applies to the search endpoint too.
        cloud.Requests[0].RequestUri!.AbsolutePath.ShouldStartWith("/rest/api/3/");
        dataCenter.Requests[0].RequestUri!.AbsolutePath.ShouldStartWith("/rest/api/2/");
    }

    [Fact]
    public async Task ListItemsInStatesAsync_Should_AskForNothingWhenNoStatesAreMapped()
    {
        var handler = new RecordingHandler(RecordingHandler.Json("""{"issues":[]}"""));

        (await Build(handler).ListItemsInStatesAsync("DSC", "", [])).ShouldBeEmpty();
        handler.Requests.ShouldBeEmpty();
    }
}
