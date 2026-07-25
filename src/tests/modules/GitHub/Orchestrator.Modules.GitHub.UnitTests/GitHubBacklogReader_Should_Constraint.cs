using System.Net;
using System.Text;
using Orchestrator.Modules.Backlog.Contracts;
using Shouldly;

namespace Orchestrator.Modules.GitHub.UnitTests;

/// <summary>
/// The gate listing the Portal shows. The behaviour worth pinning is not the JSON mapping — it is the two
/// GitHub-specific traps this reader exists to avoid.
/// </summary>
public class GitHubBacklogReader_Should_Constraint
{
    sealed class RecordingHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        int _index;

        public List<string> Paths { get; } = [];
        public List<string?> AuthSchemes { get; } = [];

        public static Func<HttpResponseMessage> Json(string json) =>
            () =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            AuthSchemes.Add(request.Headers.Authorization?.Scheme);

            var factory = responses[Math.Min(_index, responses.Length - 1)];
            _index++;

            return Task.FromResult(factory());
        }
    }

    static GitHubBacklogReader Build(RecordingHandler handler, string token = "read-only-token") =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") })
        {
            ReadTokenFor = (_, _) => Task.FromResult(token),
        };

    const string OneIssue = """
        [
          {
            "number": 42,
            "title": "Add the thing",
            "assignee": { "login": "someone" },
            "updated_at": "2026-07-25T10:00:00Z",
            "html_url": "https://github.com/acme/widgets/issues/42"
          }
        ]
        """;

    [Fact]
    public async Task ListItemsInStatesAsync_Should_QueryEachStateSeparately()
    {
        // The trap: GitHub's `labels` parameter is an AND. One call with both gate labels would return
        // only issues carrying *both*, which the single-state invariant guarantees is none -- an empty
        // gate view that looks like "nothing is queued" rather than a bug.
        var handler = new RecordingHandler(RecordingHandler.Json("[]"));

        await Build(handler)
            .ListItemsInStatesAsync(
                null,
                "acme/widgets",
                ["status:ready-for-proposal", "status:ready-for-implementation"]
            );

        handler.Paths.Count.ShouldBe(2);
        handler.Paths[0].ShouldContain("labels=status%3Aready-for-proposal");
        handler.Paths[1].ShouldContain("labels=status%3Aready-for-implementation");
    }

    [Fact]
    public async Task ListItemsInStatesAsync_Should_ExcludePullRequests()
    {
        // The issues endpoint returns PRs too. A PR carrying a gate label is not a backlog item, and
        // listing one would invite an operator to dispatch an agent at it.
        var handler = new RecordingHandler(
            RecordingHandler.Json(
                """
                [
                  { "number": 7, "title": "a PR", "pull_request": { "url": "..." } },
                  { "number": 42, "title": "an issue" }
                ]
                """
            )
        );

        var items = await Build(handler)
            .ListItemsInStatesAsync(null, "acme/widgets", ["status:ready-for-proposal"]);

        items.ShouldHaveSingleItem().ItemId.ShouldBe("42");
    }

    [Fact]
    public async Task ListItemsInStatesAsync_Should_NotDoubleCountAnItemCarryingTwoGateStates()
    {
        // The invariant says one lifecycle label at a time. The portal is exactly where you would find
        // out it had been violated, so it must surface the item without counting it twice.
        var handler = new RecordingHandler(RecordingHandler.Json(OneIssue));

        var items = await Build(handler)
            .ListItemsInStatesAsync(
                null,
                "acme/widgets",
                ["status:ready-for-proposal", "status:ready-for-implementation"]
            );

        items.ShouldHaveSingleItem().ItemId.ShouldBe("42");
    }

    [Fact]
    public async Task ListItemsInStatesAsync_Should_ProjectTheFieldsAViewNeeds()
    {
        var handler = new RecordingHandler(RecordingHandler.Json(OneIssue));

        var item = (
            await Build(handler)
                .ListItemsInStatesAsync(null, "acme/widgets", ["status:ready-for-proposal"])
        ).ShouldHaveSingleItem();

        item.Title.ShouldBe("Add the thing");
        item.NativeState.ShouldBe("status:ready-for-proposal");
        item.Assignee.ShouldBe("someone");
        item.Url!.ToString().ShouldBe("https://github.com/acme/widgets/issues/42");
    }

    [Fact]
    public async Task ListItemsInStatesAsync_Should_AskForNothingWhenNoStatesAreMapped()
    {
        var handler = new RecordingHandler(RecordingHandler.Json("[]"));

        var items = await Build(handler).ListItemsInStatesAsync(null, "acme/widgets", []);

        items.ShouldBeEmpty();
        // A target with no mapped states has no gate to show; asking GitHub anyway would spend a
        // rate-limited call to learn nothing.
        handler.Paths.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListItemsInStatesAsync_Should_UseTheSuppliedReadScopedToken()
    {
        var handler = new RecordingHandler(RecordingHandler.Json("[]"));

        await Build(handler).ListItemsInStatesAsync(null, "acme/widgets", ["status:x"]);

        // The reader never mints a token of its own -- it cannot, which is the point of D9.
        handler.AuthSchemes.ShouldAllBe(scheme => scheme == "Bearer");
    }
}
