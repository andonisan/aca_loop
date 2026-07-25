using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.AzureDevOps;

/// <summary>
/// Read-only listing of Azure DevOps work items sitting in a gate state.
/// <para>
/// Unlike the label-based vendors, this needs no request per state: WIQL expresses the whole set in one
/// <c>IN</c> clause. That is the case the <c>IBacklogReader</c> contract takes a state *collection* for —
/// a per-state signature would have forced this vendor into N queries it does not need.
/// </para>
/// <para>
/// Separate from <c>AzureDevOpsAdapter</c>, which holds a PAT with Work Items write; this takes a
/// read-scoped one (orchestrator-portal design D9).
/// </para>
/// </summary>
public sealed class AzureDevOpsBacklogReader(HttpClient httpClient) : IBacklogReader
{
    const string ApiVersion = "7.1";

    /// <summary>Batch field reads are capped by the API at 200 ids per call.</summary>
    const int BatchSize = 200;

    public required Func<string, CancellationToken, Task<string>> ReadTokenFor { get; init; }

    public Vendor Vendor => Vendor.AzureDevOps;

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

        // Boards work items are project-scoped; the adapter carries the project name in `repo` where no
        // separate project coordinate is configured, matching its documented simplifying assumption.
        var projectName = string.IsNullOrWhiteSpace(project) ? repo : project;
        var token = await ReadTokenFor(projectName, cancellationToken);

        var ids = await QueryIdsAsync(token, projectName, states, cancellationToken);
        if (ids.Count == 0)
        {
            return [];
        }

        var items = new List<BacklogItem>(ids.Count);
        foreach (var batch in ids.Chunk(BatchSize))
        {
            items.AddRange(await ReadFieldsAsync(token, projectName, batch, cancellationToken));
        }

        return [.. items.OrderByDescending(i => i.UpdatedAt ?? DateTimeOffset.MinValue)];
    }

    /// <summary>WIQL returns ids only — the fields come from a second, batched call. Two round trips for
    /// any number of states, rather than one per state.</summary>
    async Task<IReadOnlyList<long>> QueryIdsAsync(
        string token,
        string project,
        IReadOnlyCollection<string> states,
        CancellationToken cancellationToken
    )
    {
        // Single-quoted WIQL literals, with embedded quotes doubled. State names come from the mapping,
        // which operators author, so this is not untrusted input -- but a state named "Won't Do" would
        // otherwise produce a syntax error rather than a result, which is a confusing way to learn about
        // an apostrophe.
        var stateList = string.Join(", ", states.Select(s => $"'{s.Replace("'", "''")}'"));

        using var document = await SendAsync(
            token,
            HttpMethod.Post,
            $"/{Uri.EscapeDataString(project)}/_apis/wit/wiql?api-version={ApiVersion}",
            new
            {
                query = "SELECT [System.Id] FROM WorkItems "
                    + $"WHERE [System.TeamProject] = '{project.Replace("'", "''")}' "
                    + $"AND [System.State] IN ({stateList}) "
                    + "ORDER BY [System.ChangedDate] DESC",
            },
            cancellationToken
        );

        if (!document.RootElement.TryGetProperty("workItems", out var workItems))
        {
            return [];
        }

        return [.. workItems.EnumerateArray().Select(w => w.GetProperty("id").GetInt64())];
    }

    async Task<IEnumerable<BacklogItem>> ReadFieldsAsync(
        string token,
        string project,
        IReadOnlyList<long> ids,
        CancellationToken cancellationToken
    )
    {
        using var document = await SendAsync(
            token,
            HttpMethod.Post,
            $"/{Uri.EscapeDataString(project)}/_apis/wit/workitemsbatch?api-version={ApiVersion}",
            new
            {
                ids,
                fields = new[]
                {
                    "System.Id",
                    "System.Title",
                    "System.State",
                    "System.AssignedTo",
                    "System.ChangedDate",
                },
            },
            cancellationToken
        );

        var items = new List<BacklogItem>();
        if (!document.RootElement.TryGetProperty("value", out var value))
        {
            return items;
        }

        foreach (var workItem in value.EnumerateArray())
        {
            var fields = workItem.GetProperty("fields");
            items.Add(
                new BacklogItem(
                    ItemId: workItem.GetProperty("id").GetInt64().ToString(),
                    Title: Text(fields, "System.Title"),
                    NativeState: Text(fields, "System.State"),
                    // AssignedTo is an identity object, not a string, and is absent when unassigned.
                    Assignee: fields.TryGetProperty("System.AssignedTo", out var assignedTo)
                    && assignedTo.ValueKind == JsonValueKind.Object
                    && assignedTo.TryGetProperty("displayName", out var displayName)
                        ? displayName.GetString()
                        : null,
                    UpdatedAt: fields.TryGetProperty("System.ChangedDate", out var changed)
                    && changed.TryGetDateTimeOffset(out var changedAt)
                        ? changedAt
                        : null,
                    Url: BuildItemUrl(project, workItem.GetProperty("id").GetInt64())
                )
            );
        }

        return items;
    }

    static string Text(JsonElement fields, string name) =>
        fields.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

    /// <summary>The API's own <c>url</c> is the REST resource, which is useless to a human. The browser
    /// link has to be composed from the organization base address instead.</summary>
    Uri? BuildItemUrl(string project, long id) =>
        httpClient.BaseAddress is null
            ? null
            : new Uri(
                httpClient.BaseAddress,
                $"{Uri.EscapeDataString(project)}/_workitems/edit/{id}"
            );

    async Task<JsonDocument> SendAsync(
        string token,
        HttpMethod method,
        string path,
        object body,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($":{token}"))
        );

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"{method} {path} failed: {(int)response.StatusCode} {responseBody}"
            );
        }

        return await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken
        );
    }
}
