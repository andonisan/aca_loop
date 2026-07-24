using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.Jira;

/// <summary>
/// Jira vendor adapter — a tracker only. Jira hosts no git, so this implements
/// <see cref="IBacklogAdapter"/> and deliberately not <c>ICodeHostAdapter</c>; a Jira target pairs with
/// GitHub, GitLab, or Azure DevOps as its code host through the mapping's <c>codeHost</c>/<c>codeRepo</c>
/// (design.md D6).
/// <para>
/// Three things make Jira unlike the other three vendors. State lives in the workflow
/// <c>status</c> rather than a label, which gives a native single-state invariant (D4). Status cannot be
/// written directly — it moves only through a transition that is valid from the item's *current* status,
/// so write-back is a discovery step followed by an execution, and the target state can legitimately be
/// unreachable (D5). And Cloud and Data Center diverge on REST version, comment format, and auth
/// together, selected by one <see cref="JiraDeployment"/> setting (D11).
/// </para>
/// </summary>
sealed class JiraAdapter(HttpClient httpClient, IOptions<JiraOptions> options) : IBacklogAdapter
{
    const string StatusField = "status";
    const string IssueUpdatedEvent = "jira:issue_updated";

    readonly JiraOptions _options = options.Value;

    public Vendor Vendor => Vendor.Jira;

    public bool VerifyWebhook(
        ReadOnlyMemory<byte> rawBody,
        IReadOnlyDictionary<string, string> headers
    )
    {
        // Atlassian makes the webhook secret optional at registration. This adapter does not: a
        // missing signature is a rejection, never "verification not configured" (design.md D2).
        if (!headers.TryGetValue("X-Hub-Signature", out var signatureHeader))
        {
            return false;
        }

        // WebSub's "method=signature" form. Parsed rather than assumed, because Atlassian documents
        // that the HMAC method may change -- an unknown method must be rejected, not silently
        // verified with a default algorithm.
        var separator = signatureHeader.IndexOf('=');
        if (separator <= 0)
        {
            return false;
        }

        var method = signatureHeader[..separator];
        using HMAC? hmac = method.ToLowerInvariant() switch
        {
            "sha256" => new HMACSHA256(Encoding.UTF8.GetBytes(_options.WebhookSecret)),
            "sha1" => new HMACSHA1(Encoding.UTF8.GetBytes(_options.WebhookSecret)),
            _ => null,
        };
        if (hmac is null)
        {
            return false;
        }

        var expected = $"{method}={Convert.ToHexStringLower(hmac.ComputeHash(rawBody.ToArray()))}";

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signatureHeader)
        );
    }

    public CanonicalEvent? ParseEvent(
        ReadOnlyMemory<byte> rawBody,
        IReadOnlyDictionary<string, string> headers
    )
    {
        using var payload = JsonDocument.Parse(rawBody);
        var root = payload.RootElement;

        // Jira system webhooks carry the event type in the body, not a header.
        if (
            !root.TryGetProperty("webhookEvent", out var webhookEvent)
            || webhookEvent.GetString() != IssueUpdatedEvent
        )
        {
            return null;
        }

        // jira:issue_updated fires for every field change. Only a status change is a lifecycle
        // transition, and the changelog is the only place that distinguishes them.
        var newStatus = FindStatusChange(root);
        if (newStatus is null)
        {
            return null;
        }

        if (!root.TryGetProperty("issue", out var issue))
        {
            return null;
        }

        var issueKey = issue.TryGetProperty("key", out var key) ? key.GetString() : null;
        if (string.IsNullOrEmpty(issueKey))
        {
            return null;
        }

        var projectKey =
            issue.TryGetProperty("fields", out var fields)
            && fields.TryGetProperty("project", out var project)
            && project.TryGetProperty("key", out var projectKeyElement)
                ? projectKeyElement.GetString()
                : null;

        return new CanonicalEvent(
            Vendor.Jira,
            Project: projectKey,
            // A Jira issue belongs to a project, not a repository. The mapping's codeRepo identifies
            // what to clone; this coordinate has nothing to carry.
            Repo: string.Empty,
            ItemId: issueKey,
            NativeState: newStatus,
            Actor: ReadActor(root)
        );
    }

    /// <summary>The <c>toString</c> of the changelog entry whose field is <c>status</c>, or null when
    /// the update did not touch status. <c>to</c> carries the status id alongside it; the name is what
    /// the mapping keys on, matching how every other vendor keys on a human-readable string (D9).</summary>
    static string? FindStatusChange(JsonElement root)
    {
        if (
            !root.TryGetProperty("changelog", out var changelog)
            || !changelog.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array
        )
        {
            return null;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (
                item.TryGetProperty("field", out var field)
                && string.Equals(field.GetString(), StatusField, StringComparison.OrdinalIgnoreCase)
                && item.TryGetProperty("toString", out var toString)
            )
            {
                var value = toString.GetString();
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    /// <summary>Prefers <c>accountId</c> (Cloud) and falls back to <c>name</c> (Data Center, which has
    /// no account ids). Never the display name: those are mutable and not unique.</summary>
    static string? ReadActor(JsonElement root)
    {
        if (!root.TryGetProperty("user", out var user))
        {
            return null;
        }

        if (user.TryGetProperty("accountId", out var accountId))
        {
            return accountId.GetString();
        }

        return user.TryGetProperty("name", out var name) ? name.GetString() : null;
    }

    public bool IsAutomationActor(string? actor) =>
        actor is not null
        && string.Equals(actor, _options.BotAccountId, StringComparison.OrdinalIgnoreCase);

    public async Task<string> ReadItemStateAsync(
        string? project,
        string repo,
        string itemId,
        CancellationToken cancellationToken = default
    )
    {
        using var document = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, Path($"issue/{itemId}?fields=status")),
            cancellationToken
        );

        return document
                ?.RootElement.GetProperty("fields")
                .GetProperty(StatusField)
                .GetProperty("name")
                .GetString()
            ?? string.Empty;
    }

    /// <summary>
    /// Jira cannot set status as a field: it moves only through a workflow transition valid from the
    /// item's current status. So this discovers the available transitions, matches the requested state
    /// against their target status names, and executes that transition by id.
    /// <para>
    /// Throws when no transition reaches the requested state. That is a legitimate workflow outcome
    /// rather than a bug, and Execution's failure path treats it as such: the failure comment is
    /// already posted by then, and this becomes context on the original failure rather than replacing
    /// it (design.md D5).
    /// </para>
    /// </summary>
    public async Task TransitionStateAsync(
        string? project,
        string repo,
        string itemId,
        string newState,
        CancellationToken cancellationToken = default
    )
    {
        using var document = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, Path($"issue/{itemId}/transitions")),
            cancellationToken
        );

        var transitionId = FindTransitionTo(document, newState);
        if (transitionId is null)
        {
            var available = AvailableStatusNames(document);
            throw new InvalidOperationException(
                $"no transition from the current status of {itemId} reaches '{newState}'"
                    + $" (available: {(available.Length == 0 ? "none" : string.Join(", ", available))})"
            );
        }

        using var _ = await SendAsync(
            () =>
                new HttpRequestMessage(HttpMethod.Post, Path($"issue/{itemId}/transitions"))
                {
                    Content = JsonContent.Create(new { transition = new { id = transitionId } }),
                },
            cancellationToken
        );
    }

    static string? FindTransitionTo(JsonDocument? document, string targetStatus)
    {
        if (document is null || !document.RootElement.TryGetProperty("transitions", out var list))
        {
            return null;
        }

        foreach (var transition in list.EnumerateArray())
        {
            if (
                transition.TryGetProperty("to", out var to)
                && to.TryGetProperty("name", out var name)
                && string.Equals(name.GetString(), targetStatus, StringComparison.OrdinalIgnoreCase)
                && transition.TryGetProperty("id", out var id)
            )
            {
                return id.GetString();
            }
        }

        return null;
    }

    /// <summary>Target status names of the currently-available transitions — included in the failure
    /// message because "no transition reaches X" is only actionable alongside what does.</summary>
    static string[] AvailableStatusNames(JsonDocument? document)
    {
        if (document is null || !document.RootElement.TryGetProperty("transitions", out var list))
        {
            return [];
        }

        return
        [
            .. list.EnumerateArray()
                .Select(t =>
                    t.TryGetProperty("to", out var to) && to.TryGetProperty("name", out var name)
                        ? name.GetString()
                        : null
                )
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!),
        ];
    }

    public async Task PostCommentAsync(
        string? project,
        string repo,
        string itemId,
        string body,
        CancellationToken cancellationToken = default
    )
    {
        object payload =
            _options.Deployment == JiraDeployment.Cloud
                ? new { body = BuildAdfDocument(body) }
                // Data Center's v2 endpoints take plain text with optional wiki markup; Atlassian
                // Document Format does not exist there (design.md D11).
                : new { body };

        using var _ = await SendAsync(
            () =>
                new HttpRequestMessage(HttpMethod.Post, Path($"issue/{itemId}/comment"))
                {
                    Content = JsonContent.Create(payload),
                },
            cancellationToken
        );
    }

    /// <summary>
    /// The minimal Atlassian Document Format wrapper v3 requires. One paragraph per line: ADF has no
    /// concept of a newline inside a text node, so a multi-line log tail would otherwise collapse onto
    /// one line. Blank lines are dropped rather than emitted as empty paragraphs.
    /// </summary>
    static object BuildAdfDocument(string body) =>
        new
        {
            type = "doc",
            version = 1,
            content = body.ReplaceLineEndings("\n")
                .Split('\n')
                .Where(line => line.Length > 0)
                .Select(line => new
                {
                    type = "paragraph",
                    content = new[] { new { type = "text", text = line } },
                })
                .ToArray(),
        };

    string Path(string resource) => $"/rest/api/{_options.ApiVersion}/{resource}";

    /// <summary>
    /// Every outbound call goes through here so the retry policy cannot be forgotten on one of them.
    /// Retries on 429 and 5xx with <c>Retry-After</c> honored when present, exponential backoff plus
    /// jitter otherwise, and bounded attempts (design.md D12). Takes a factory rather than a request
    /// because an <see cref="HttpRequestMessage"/> cannot be sent twice.
    /// </summary>
    async Task<JsonDocument?> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = requestFactory();
            Authenticate(request);

            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                if (response.Content.Headers.ContentLength == 0)
                {
                    return null;
                }

                var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }

            if (!IsRetryable(response.StatusCode) || attempt >= _options.MaxRetries)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"{request.Method} {request.RequestUri} failed: {(int)response.StatusCode} {body}"
                );
            }

            await Task.Delay(RetryDelay(response, attempt), cancellationToken);
        }
    }

    static bool IsRetryable(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        // Jira sends Retry-After on 429; obeying it is not optional -- backing off less aggressively
        // than asked is how a caller gets throttled harder.
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }
        if (retryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
            {
                return until;
            }
        }

        // Exponential from the configured base (1s, 2s, 4s, 8s by default), plus jitter so concurrent
        // replicas do not retry in lockstep.
        var baseDelay = _options.RetryBaseDelayMilliseconds;
        if (baseDelay <= 0)
        {
            return TimeSpan.Zero;
        }

        var backoff = TimeSpan.FromMilliseconds(baseDelay * Math.Pow(2, attempt));
        return backoff + TimeSpan.FromMilliseconds(Random.Shared.Next(0, baseDelay));
    }

    void Authenticate(HttpRequestMessage request) =>
        request.Headers.Authorization =
            _options.Deployment == JiraDeployment.Cloud
                ? new AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(
                        Encoding.UTF8.GetBytes($"{_options.AccountEmail}:{_options.ApiToken}")
                    )
                )
                // Data Center personal access tokens are bearer tokens, not Basic credentials.
                : new AuthenticationHeaderValue("Bearer", _options.ApiToken);
}
