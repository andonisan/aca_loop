using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.Jira;

/// <summary>
/// Read-only listing of Jira issues sitting in a gate status.
/// <para>
/// Like Azure DevOps and unlike the label-based vendors, one query covers every state: JQL takes
/// <c>status IN (...)</c>. Jira is also the vendor where per-state querying would hurt most, since its
/// rate limits are the tightest of the four.
/// </para>
/// <para>
/// Separate from <c>JiraAdapter</c>, which holds a token permitted to transition issues; this one takes a
/// browse-only account's token (orchestrator-portal design D9).
/// </para>
/// </summary>
public sealed class JiraBacklogReader(HttpClient httpClient, IOptions<JiraOptions> options)
    : IBacklogReader
{
    const int MaxResults = 100;

    readonly JiraOptions _options = options.Value;

    public required Func<string, CancellationToken, Task<string>> ReadTokenFor { get; init; }

    public Vendor Vendor => Vendor.Jira;

    public async Task<IReadOnlyList<BacklogItem>> ListItemsInStatesAsync(
        string? project,
        string repo,
        IReadOnlyCollection<string> states,
        CancellationToken cancellationToken = default
    )
    {
        if (states.Count == 0 || string.IsNullOrWhiteSpace(project))
        {
            // A Jira target is identified by its project key; without one there is nothing to scope a
            // JQL query to, and an unscoped query would list every issue the account can see.
            return [];
        }

        var token = await ReadTokenFor(project, cancellationToken);

        // Double-quoted JQL literals with embedded quotes escaped. Status names come from the mapping,
        // which operators author -- but they routinely contain spaces and occasionally punctuation
        // ("In Review / Deployment" is a real one from the vocabulary catalog), so quoting is required
        // rather than defensive.
        var statusList = string.Join(
            ", ",
            states.Select(s => $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"")
        );
        var jql =
            $"project = \"{project.Replace("\"", "\\\"")}\" AND status IN ({statusList}) "
            + "ORDER BY updated DESC";

        using var document = await GetAsync(
            token,
            $"/rest/api/{_options.ApiVersion}/search"
                + $"?jql={Uri.EscapeDataString(jql)}"
                + $"&maxResults={MaxResults}"
                + "&fields=summary,status,assignee,updated",
            cancellationToken
        );

        if (!document.RootElement.TryGetProperty("issues", out var issues))
        {
            return [];
        }

        return [.. issues.EnumerateArray().Select(ToItem)];
    }

    public async Task<string> ReadItemStateAsync(
        string? project,
        string repo,
        string itemId,
        CancellationToken cancellationToken = default
    )
    {
        var token = await ReadTokenFor(project ?? "", cancellationToken);

        using var document = await GetAsync(
            token,
            $"/rest/api/{_options.ApiVersion}/issue/{itemId}?fields=status",
            cancellationToken
        );

        return
            document.RootElement.TryGetProperty("fields", out var fields)
            && fields.TryGetProperty("status", out var status)
            && status.TryGetProperty("name", out var name)
            ? name.GetString() ?? string.Empty
            : string.Empty;
    }

    /// <summary>Keyed by issue key, matching what the adapter uses as <c>ItemId</c> and what a human
    /// quotes — never the numeric id.</summary>
    BacklogItem ToItem(JsonElement issue)
    {
        var fields = issue.GetProperty("fields");
        var key = issue.GetProperty("key").GetString()!;

        return new BacklogItem(
            ItemId: key,
            Title: fields.TryGetProperty("summary", out var summary)
                ? summary.GetString() ?? ""
                : "",
            NativeState: fields.TryGetProperty("status", out var status)
            && status.TryGetProperty("name", out var statusName)
                ? statusName.GetString() ?? ""
                : "",
            Assignee: fields.TryGetProperty("assignee", out var assignee)
            && assignee.ValueKind == JsonValueKind.Object
            && assignee.TryGetProperty("displayName", out var displayName)
                ? displayName.GetString()
                : null,
            UpdatedAt: fields.TryGetProperty("updated", out var updated)
            && updated.TryGetDateTimeOffset(out var updatedAt)
                ? updatedAt
                : null,
            Url: Uri.TryCreate(
                _options.BaseUrl.TrimEnd('/') + $"/browse/{key}",
                UriKind.Absolute,
                out var url
            )
                ? url
                : null
        );
    }

    async Task<JsonDocument> GetAsync(
        string token,
        string path,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        // Same Cloud/Data Center split the adapter makes, for the same reason: Cloud is Basic with the
        // account email, Data Center is a bearer personal access token (jira-adapter design D11).
        request.Headers.Authorization =
            _options.Deployment == JiraDeployment.Cloud
                ? new AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(
                        Encoding.UTF8.GetBytes($"{_options.AccountEmail}:{token}")
                    )
                )
                : new AuthenticationHeaderValue("Bearer", token);

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
