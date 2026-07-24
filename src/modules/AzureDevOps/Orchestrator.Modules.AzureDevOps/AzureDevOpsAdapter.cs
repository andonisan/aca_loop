using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.AzureDevOps;

/// <summary>
/// Azure DevOps vendor adapter. Service Hooks authenticate via HTTP Basic auth (a configured
/// username/password), not a signature scheme. A "Work item updated" event only matters to us when
/// the update touched <c>System.State</c> — every other field change on the same event type is
/// ignored. Write-back sets the work item's <c>System.State</c> field directly (single-valued by
/// construction, unlike GitHub/GitLab's label-based invariant — design.md D4).
/// </summary>
sealed class AzureDevOpsAdapter(HttpClient httpClient, IOptions<AzureDevOpsOptions> options) : IVendorAdapter
{
    const string ApiVersion = "7.1";

    readonly AzureDevOpsOptions _options = options.Value;

    public Vendor Vendor => Vendor.AzureDevOps;

    public bool VerifyWebhook(ReadOnlyMemory<byte> rawBody, IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("Authorization", out var authorization))
        {
            return false;
        }

        var expected =
            "Basic "
            + Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_options.WebhookUsername}:{_options.WebhookPassword}")
            );

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(authorization)
        );
    }

    public CanonicalEvent? ParseEvent(
        ReadOnlyMemory<byte> rawBody,
        IReadOnlyDictionary<string, string> headers
    )
    {
        using var payload = JsonDocument.Parse(rawBody);
        var root = payload.RootElement;

        if (root.GetProperty("eventType").GetString() != "workitem.updated")
        {
            return null;
        }

        var resource = root.GetProperty("resource");

        if (
            !resource.TryGetProperty("fields", out var changedFields)
            || !changedFields.TryGetProperty("System.State", out var stateChange)
            || !stateChange.TryGetProperty("newValue", out var newStateElement)
        )
        {
            // A work item update that didn't touch State -- not a lifecycle transition.
            return null;
        }

        var workItemId = (
            resource.TryGetProperty("workItemId", out var workItemIdElement)
                ? workItemIdElement
                : resource.GetProperty("id")
        ).GetInt64();

        var project = resource.TryGetProperty("revision", out var revision)
            ? revision.GetProperty("fields").GetProperty("System.TeamProject").GetString()
            : null;

        var actor = resource.TryGetProperty("revisedBy", out var revisedBy)
            ? (revisedBy.TryGetProperty("uniqueName", out var uniqueName) ? uniqueName.GetString() : null)
                ?? (revisedBy.TryGetProperty("displayName", out var displayName) ? displayName.GetString() : null)
            : null;

        if (project is null)
        {
            return null;
        }

        return new CanonicalEvent(
            Vendor.AzureDevOps,
            Project: _options.Organization,
            Repo: project,
            ItemId: workItemId.ToString(),
            NativeState: newStateElement.GetString()!,
            Actor: actor
        );
    }

    public bool IsAutomationActor(string? actor) =>
        actor is not null && string.Equals(actor, _options.BotIdentity, StringComparison.OrdinalIgnoreCase);

    public async Task<string> ReadItemStateAsync(
        string? project,
        string repo,
        string itemId,
        CancellationToken cancellationToken = default
    )
    {
        using var document = await GetAsync(
            $"/_apis/wit/workitems/{itemId}?api-version={ApiVersion}",
            cancellationToken
        );
        return document.RootElement.GetProperty("fields").GetProperty("System.State").GetString()!;
    }

    public async Task TransitionStateAsync(
        string? project,
        string repo,
        string itemId,
        string newState,
        CancellationToken cancellationToken = default
    ) =>
        await PatchAsync(
            $"/_apis/wit/workitems/{itemId}?api-version={ApiVersion}",
            new object[] { new { op = "add", path = "/fields/System.State", value = newState } },
            cancellationToken
        );

    public async Task PostCommentAsync(
        string? project,
        string repo,
        string itemId,
        string body,
        CancellationToken cancellationToken = default
    ) =>
        await SendAsync(
            HttpMethod.Post,
            $"/_apis/wit/workItems/{itemId}/comments?api-version={ApiVersion}-preview.3",
            new { text = body },
            cancellationToken
        );

    public Task<CloneCredentials> GetCloneCredentialsAsync(
        string? project,
        string repo,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            new CloneCredentials(
                Token: _options.PersonalAccessToken,
                CloneUrl: $"https://pat:{_options.PersonalAccessToken}@dev.azure.com/{_options.Organization}/{repo}/_git/{repo}",
                GitAuthorName: _options.BotIdentity,
                GitAuthorEmail: _options.BotIdentity
            )
        );

    static AuthenticationHeaderValue BuildPatAuthHeader(string pat) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($":{pat}")));

    async Task<JsonDocument> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = BuildPatAuthHeader(_options.PersonalAccessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"GET {path} failed: {(int)response.StatusCode} {body}");
        }

        return await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken
        );
    }

    async Task PatchAsync(string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Content!.Headers.ContentType = new MediaTypeHeaderValue("application/json-patch+json");
        request.Headers.Authorization = BuildPatAuthHeader(_options.PersonalAccessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body2 = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"PATCH {path} failed: {(int)response.StatusCode} {body2}");
        }
    }

    async Task SendAsync(
        HttpMethod method,
        string path,
        object body,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = BuildPatAuthHeader(_options.PersonalAccessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"{method} {path} failed: {(int)response.StatusCode} {responseBody}"
            );
        }
    }
}
