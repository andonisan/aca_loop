using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.GitHub;

/// <summary>
/// GitHub vendor adapter — ported from the v1 Node entrypoint (<c>webhook-receiver/src/index.js</c>,
/// <c>agent/src/github.js</c>). Signature verification, <c>labeled</c> event parsing, bot
/// self-filtering, label write-back preserving the single-status-label invariant, and per-run
/// installation-token minting.
/// </summary>
sealed class GitHubAdapter(HttpClient httpClient, IOptions<GitHubOptions> options)
    : IBacklogAdapter,
        ICodeHostAdapter
{
    const string GitHubApiVersion = "2022-11-28";
    const string StatusLabelPrefix = "status:";

    readonly GitHubOptions _options = options.Value;

    public Vendor Vendor => Vendor.GitHub;

    public bool VerifyWebhook(
        ReadOnlyMemory<byte> rawBody,
        IReadOnlyDictionary<string, string> headers
    )
    {
        if (!headers.TryGetValue("X-Hub-Signature-256", out var signatureHeader))
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.WebhookSecret));
        var computed = hmac.ComputeHash(rawBody.ToArray());
        var expected = "sha256=" + Convert.ToHexStringLower(computed);

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
        if (!headers.TryGetValue("X-GitHub-Event", out var eventType) || eventType != "issues")
        {
            return null;
        }

        using var payload = JsonDocument.Parse(rawBody);
        var root = payload.RootElement;

        if (root.GetProperty("action").GetString() != "labeled")
        {
            return null;
        }

        var appliedLabel = root.GetProperty("label").GetProperty("name").GetString();
        if (string.IsNullOrEmpty(appliedLabel))
        {
            return null;
        }

        var repoFullName = root.GetProperty("repository").GetProperty("full_name").GetString()!;
        var issueNumber = root.GetProperty("issue").GetProperty("number").GetInt64();
        var senderLogin = root.TryGetProperty("sender", out var sender)
            ? sender.GetProperty("login").GetString()
            : null;

        return new CanonicalEvent(
            Vendor.GitHub,
            Project: null,
            Repo: repoFullName,
            ItemId: issueNumber.ToString(),
            NativeState: appliedLabel,
            Actor: senderLogin
        );
    }

    public bool IsAutomationActor(string? actor) =>
        actor is not null
        && string.Equals(actor, _options.BotLogin, StringComparison.OrdinalIgnoreCase);

    public async Task<string> ReadItemStateAsync(
        string? project,
        string repo,
        string itemId,
        CancellationToken cancellationToken = default
    )
    {
        var token = await GetInstallationTokenAsync(repo, cancellationToken);
        var labels = await GetIssueLabelsAsync(token, repo, itemId, cancellationToken);
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
        var token = await GetInstallationTokenAsync(repo, cancellationToken);
        var labels = await GetIssueLabelsAsync(token, repo, itemId, cancellationToken);

        var newLabels = labels
            .Where(l => !l.StartsWith(StatusLabelPrefix, StringComparison.Ordinal))
            .Append(newState)
            .ToArray();

        await SendAsync(
            token,
            HttpMethod.Put,
            $"/repos/{repo}/issues/{itemId}/labels",
            new { labels = newLabels },
            cancellationToken
        );
    }

    public async Task PostCommentAsync(
        string? project,
        string repo,
        string itemId,
        string body,
        CancellationToken cancellationToken = default
    )
    {
        var token = await GetInstallationTokenAsync(repo, cancellationToken);
        await SendAsync(
            token,
            HttpMethod.Post,
            $"/repos/{repo}/issues/{itemId}/comments",
            new { body },
            cancellationToken
        );
    }

    public async Task<CloneCredentials> GetCloneCredentialsAsync(
        string? project,
        string repo,
        CancellationToken cancellationToken = default
    )
    {
        var token = await GetInstallationTokenAsync(repo, cancellationToken);
        return new CloneCredentials(
            Token: token,
            CloneUrl: $"https://x-access-token:{token}@github.com/{repo}.git",
            GitAuthorName: _options.BotLogin,
            GitAuthorEmail: $"{_options.AppId}+{_options.BotLogin}@users.noreply.github.com"
        );
    }

    /// <summary>Both variables because the target repo's command may drive either <c>gh</c>
    /// (<c>GH_TOKEN</c>) or a tool reading the conventional <c>GITHUB_TOKEN</c>.</summary>
    public IReadOnlyDictionary<string, string> GetCliEnvironment(CloneCredentials credentials) =>
        new Dictionary<string, string>
        {
            ["GH_TOKEN"] = credentials.Token,
            ["GITHUB_TOKEN"] = credentials.Token,
        };

    /// <summary>Mints an installation token scoped to the single target repository — short-lived
    /// (&lt;=1h, GitHub-enforced) and never persisted.</summary>
    async Task<string> GetInstallationTokenAsync(
        string repoFullName,
        CancellationToken cancellationToken
    )
    {
        var repoName = repoFullName.Split('/')[1];
        var jwt = BuildAppJwt();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/app/installations/{_options.InstallationId}/access_tokens"
        )
        {
            Content = JsonContent.Create(new { repositories = new[] { repoName } }),
        };
        request.Headers.Authorization = new("Bearer", jwt);
        request.Headers.Add("Accept", "application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", GitHubApiVersion);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"installation token exchange failed: {(int)response.StatusCode} {body}"
            );
        }

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken
        );
        return document.RootElement.GetProperty("token").GetString()!;
    }

    string BuildAppJwt()
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(_options.PrivateKeyPem);

        var now = DateTime.UtcNow;
        var handler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            IssuedAt = now.AddSeconds(-60),
            Expires = now.AddSeconds(540),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Iss] = _options.AppId,
            },
            SigningCredentials = new SigningCredentials(
                new RsaSecurityKey(rsa) { KeyId = _options.AppId },
                SecurityAlgorithms.RsaSha256
            ),
        };

        return handler.CreateEncodedJwt(descriptor);
    }

    async Task<string[]> GetIssueLabelsAsync(
        string token,
        string repoFullName,
        string issueNumber,
        CancellationToken cancellationToken
    )
    {
        using var document = await GetAsync(
            token,
            $"/repos/{repoFullName}/issues/{issueNumber}",
            cancellationToken
        );
        return
        [
            .. document
                .RootElement.GetProperty("labels")
                .EnumerateArray()
                .Select(l => l.GetProperty("name").GetString()!),
        ];
    }

    async Task<JsonDocument> GetAsync(
        string token,
        string path,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.Add("Accept", "application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", GitHubApiVersion);

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

    async Task SendAsync(
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
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.Add("Accept", "application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", GitHubApiVersion);

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
