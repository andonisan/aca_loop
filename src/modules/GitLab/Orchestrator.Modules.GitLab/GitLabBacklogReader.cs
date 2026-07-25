using System.Text.Json;
using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.GitLab;

/// <summary>
/// Read-only listing of GitLab issues carrying a gate label.
/// <para>
/// Separate from <c>GitLabAdapter</c>, which holds a token with <c>write_repository</c>; this one takes a
/// <c>read_api</c> token it cannot escalate (orchestrator-portal design D9). Like GitHub's, the token
/// arrives per call because it belongs to a stack, not to the module.
/// </para>
/// </summary>
public sealed class GitLabBacklogReader(HttpClient httpClient) : IBacklogReader
{
    const int PerPage = 100;
    const string StatusLabelPrefix = "status:";

    public required Func<string, CancellationToken, Task<string>> ReadTokenFor { get; init; }

    public Vendor Vendor => Vendor.GitLab;

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

        // One request per state, same reason as GitHub: GitLab's `labels` parameter is also an AND, so a
        // comma-joined list returns only issues carrying every gate label at once — which the
        // single-state invariant guarantees is none.
        foreach (var state in states)
        {
            using var document = await GetAsync(
                token,
                $"/projects/{Uri.EscapeDataString(repo)}/issues"
                    + $"?state=opened&per_page={PerPage}&labels={Uri.EscapeDataString(state)}",
                cancellationToken
            );

            foreach (var issue in document.RootElement.EnumerateArray())
            {
                var item = ToItem(issue, state);
                items[item.ItemId] = item;
            }
        }

        return [.. items.Values.OrderByDescending(i => i.UpdatedAt ?? DateTimeOffset.MinValue)];
    }

    public async Task<string> ReadItemStateAsync(
        string? project,
        string repo,
        string itemId,
        CancellationToken cancellationToken = default
    )
    {
        var token = await ReadTokenFor(repo, cancellationToken);
        using var document = await GetAsync(
            token,
            $"/projects/{Uri.EscapeDataString(repo)}/issues/{itemId}",
            cancellationToken
        );

        return document
                .RootElement.GetProperty("labels")
                .EnumerateArray()
                .Select(l => l.GetString())
                .FirstOrDefault(n =>
                    n is not null && n.StartsWith(StatusLabelPrefix, StringComparison.Ordinal)
                )
            ?? string.Empty;
    }

    /// <summary>The <c>iid</c>, not the <c>id</c>: the project-scoped number is what the UI shows, what a
    /// human quotes, and what the adapter's own event parsing uses as <c>ItemId</c>. Using the global id
    /// here would make the gate view's identifiers fail to match the rest of the system's.</summary>
    static BacklogItem ToItem(JsonElement issue, string state) =>
        new(
            ItemId: issue.GetProperty("iid").GetInt64().ToString(),
            Title: issue.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
            NativeState: state,
            Assignee: issue.TryGetProperty("assignee", out var assignee)
            && assignee.ValueKind == JsonValueKind.Object
                ? assignee.GetProperty("username").GetString()
                : null,
            UpdatedAt: issue.TryGetProperty("updated_at", out var updated)
            && updated.TryGetDateTimeOffset(out var updatedAt)
                ? updatedAt
                : null,
            Url: issue.TryGetProperty("web_url", out var url)
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
        request.Headers.Add("PRIVATE-TOKEN", token);

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
