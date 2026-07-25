using System.Text.Json;
using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.GitHub;

/// <summary>
/// Read-only listing of GitHub issues carrying a gate label.
/// <para>
/// Separate from <c>GitHubAdapter</c> on purpose: that type mints write-capable installation tokens, and
/// this one is handed a read-scoped token it cannot exchange for anything more (orchestrator-portal design
/// D9). The token arrives per call rather than per instance because it belongs to a stack, and one portal
/// serves many stacks with different installations.
/// </para>
/// </summary>
public sealed class GitHubBacklogReader(HttpClient httpClient) : IBacklogReader
{
    const string ApiVersion = "2022-11-28";

    /// <summary>GitHub caps a page at 100. Gate states hold work in flight, so a target with more than
    /// this in one state has a problem the portal should surface rather than paginate around.</summary>
    const int PerPage = 100;

    /// <summary>Supplies the read-scoped token for a given repo. A delegate rather than an option so the
    /// per-stack secret is resolved by whoever owns stack configuration, not by this module.</summary>
    public required Func<string, CancellationToken, Task<string>> ReadTokenFor { get; init; }

    public Vendor Vendor => Vendor.GitHub;

    public async Task<IReadOnlyList<BacklogItem>> ListItemsInStatesAsync(
        string? project,
        string repo,
        IReadOnlyCollection<string> states,
        CancellationToken cancellationToken = default
    )
    {
        if (states.Count == 0)
        {
            return [];
        }

        var token = await ReadTokenFor(repo, cancellationToken);
        var items = new Dictionary<string, BacklogItem>(StringComparer.Ordinal);

        // One request per state rather than one with a comma-joined labels filter: GitHub's `labels`
        // parameter is an AND, so a single call would return only issues carrying *every* gate state at
        // once -- which the single-state invariant means is none of them.
        foreach (var state in states)
        {
            using var document = await GetAsync(
                token,
                $"/repos/{repo}/issues?state=open&per_page={PerPage}&labels={Uri.EscapeDataString(state)}",
                cancellationToken
            );

            foreach (var issue in document.RootElement.EnumerateArray())
            {
                // Pull requests come back from the issues endpoint too, and are not backlog items.
                if (issue.TryGetProperty("pull_request", out _))
                {
                    continue;
                }

                var item = ToItem(issue, state);

                // Keyed, because an item that somehow carries two gate labels would otherwise appear
                // twice. The invariant says that cannot happen; the portal is exactly where you would
                // find out it did, so it must not double-count while showing it.
                items[item.ItemId] = item;
            }
        }

        return [.. items.Values.OrderByDescending(i => i.UpdatedAt ?? DateTimeOffset.MinValue)];
    }

    static BacklogItem ToItem(JsonElement issue, string state) =>
        new(
            ItemId: issue.GetProperty("number").GetInt64().ToString(),
            Title: issue.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
            NativeState: state,
            Assignee: issue.TryGetProperty("assignee", out var assignee)
            && assignee.ValueKind == JsonValueKind.Object
                ? assignee.GetProperty("login").GetString()
                : null,
            UpdatedAt: issue.TryGetProperty("updated_at", out var updated)
            && updated.TryGetDateTimeOffset(out var updatedAt)
                ? updatedAt
                : null,
            Url: issue.TryGetProperty("html_url", out var url)
            && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var parsed)
                ? parsed
                : null
        );

    async Task<JsonDocument> GetAsync(
        string token,
        string path,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.Add("Accept", "application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"GET {path} failed: {(int)response.StatusCode} {body}"
            );
        }

        return await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken
        );
    }
}
