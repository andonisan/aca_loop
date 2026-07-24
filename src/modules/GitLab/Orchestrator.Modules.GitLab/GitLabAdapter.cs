using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.GitLab;

/// <summary>
/// GitLab vendor adapter. GitLab has no "labeled" action like GitHub's issues webhook — an Issue
/// Hook fires on every update, so a label addition is detected by diffing
/// <c>object_attributes.action</c>'s sibling <c>changes.labels.previous/current</c>. Authenticity is
/// a static shared-secret header (<c>X-Gitlab-Token</c>), not HMAC. State write-back reuses the
/// same "swap the status:* label" convention as GitHub (backlog-abstraction spec).
/// </summary>
sealed class GitLabAdapter(HttpClient httpClient, IOptions<GitLabOptions> options) : IVendorAdapter
{
    const string StatusLabelPrefix = "status:";

    readonly GitLabOptions _options = options.Value;

    public Vendor Vendor => Vendor.GitLab;

    public bool VerifyWebhook(ReadOnlyMemory<byte> rawBody, IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("X-Gitlab-Token", out var token))
        {
            return false;
        }

        // A static shared secret, not HMAC (GitLab's webhook auth model) -- still a fixed-time
        // compare so response timing can't leak the secret byte-by-byte.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token),
            Encoding.UTF8.GetBytes(_options.WebhookSecret)
        );
    }

    public CanonicalEvent? ParseEvent(
        ReadOnlyMemory<byte> rawBody,
        IReadOnlyDictionary<string, string> headers
    )
    {
        if (!headers.TryGetValue("X-Gitlab-Event", out var eventType) || eventType != "Issue Hook")
        {
            return null;
        }

        using var payload = JsonDocument.Parse(rawBody);
        var root = payload.RootElement;

        var addedLabel = FindAddedLabel(root);
        if (addedLabel is null)
        {
            return null;
        }

        var project = root.GetProperty("project").GetProperty("path_with_namespace").GetString()!;
        var iid = root.GetProperty("object_attributes").GetProperty("iid").GetInt64();
        var username = root.TryGetProperty("user", out var user)
            ? user.GetProperty("username").GetString()
            : null;

        return new CanonicalEvent(
            Vendor.GitLab,
            Project: null,
            Repo: project,
            ItemId: iid.ToString(),
            NativeState: addedLabel,
            Actor: username
        );
    }

    static string? FindAddedLabel(JsonElement root)
    {
        if (
            !root.TryGetProperty("changes", out var changes)
            || !changes.TryGetProperty("labels", out var labels)
        )
        {
            return null;
        }

        var previous = LabelTitles(labels, "previous");
        var current = LabelTitles(labels, "current");

        return current.Except(previous).FirstOrDefault();
    }

    static IReadOnlyCollection<string> LabelTitles(JsonElement labelsChange, string propertyName) =>
        labelsChange.TryGetProperty(propertyName, out var array) && array.ValueKind == JsonValueKind.Array
            ? [.. array.EnumerateArray().Select(l => l.GetProperty("title").GetString()!)]
            : [];

    public bool IsAutomationActor(string? actor) =>
        actor is not null && string.Equals(actor, _options.BotUsername, StringComparison.OrdinalIgnoreCase);

    public async Task<string> ReadItemStateAsync(
        string? project,
        string repo,
        string itemId,
        CancellationToken cancellationToken = default
    )
    {
        var labels = await GetIssueLabelsAsync(repo, itemId, cancellationToken);
        return labels.FirstOrDefault(l => l.StartsWith(StatusLabelPrefix, StringComparison.Ordinal))
            ?? string.Empty;
    }

    public async Task TransitionStateAsync(
        string? project,
        string repo,
        string itemId,
        string newState,
        CancellationToken cancellationToken = default
    )
    {
        var labels = await GetIssueLabelsAsync(repo, itemId, cancellationToken);
        var newLabels = labels
            .Where(l => !l.StartsWith(StatusLabelPrefix, StringComparison.Ordinal))
            .Append(newState);

        await SendAsync(
            HttpMethod.Put,
            $"/projects/{Encode(repo)}/issues/{itemId}",
            new { labels = string.Join(',', newLabels) },
            cancellationToken
        );
    }

    public async Task PostCommentAsync(
        string? project,
        string repo,
        string itemId,
        string body,
        CancellationToken cancellationToken = default
    ) =>
        await SendAsync(
            HttpMethod.Post,
            $"/projects/{Encode(repo)}/issues/{itemId}/notes",
            new { body },
            cancellationToken
        );

    public Task<CloneCredentials> GetCloneCredentialsAsync(
        string? project,
        string repo,
        CancellationToken cancellationToken = default
    )
    {
        var host = new Uri(_options.BaseUrl).Host;
        return Task.FromResult(
            new CloneCredentials(
                Token: _options.AccessToken,
                CloneUrl: $"https://oauth2:{_options.AccessToken}@{host}/{repo}.git",
                GitAuthorName: _options.BotUsername,
                GitAuthorEmail: $"{_options.BotUsername}@users.noreply.gitlab.com"
            )
        );
    }

    async Task<string[]> GetIssueLabelsAsync(string repo, string itemId, CancellationToken cancellationToken)
    {
        using var document = await GetAsync($"/projects/{Encode(repo)}/issues/{itemId}", cancellationToken);
        return
        [
            .. document.RootElement.GetProperty("labels").EnumerateArray().Select(l => l.GetString()!),
        ];
    }

    async Task<JsonDocument> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("PRIVATE-TOKEN", _options.AccessToken);

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

    async Task SendAsync(HttpMethod method, string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("PRIVATE-TOKEN", _options.AccessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"{method} {path} failed: {(int)response.StatusCode} {responseBody}"
            );
        }
    }

    static string Encode(string repo) => Uri.EscapeDataString(repo);
}
