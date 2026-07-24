namespace Orchestrator.Modules.GitLab;

/// <summary>Configuration for the GitLab vendor adapter. Bound from the <c>Vendors:GitLab</c>
/// configuration section; <see cref="AccessToken"/> and <see cref="WebhookSecret"/> are expected to
/// be sourced from Key Vault at the host level, never a plain appsettings value.</summary>
public sealed class GitLabOptions
{
    public const string SectionName = "Vendors:GitLab";

    /// <summary>API host, e.g. "https://gitlab.com" or a self-managed instance's URL.</summary>
    public string BaseUrl { get; init; } = "https://gitlab.com";

    public required string BotUsername { get; init; }

    /// <summary>Project or group access token (GitLab has no App-equivalent JIT token minting — see
    /// design.md D6), scoped to the minimum (api, write_repository) per project.</summary>
    public required string AccessToken { get; init; }

    public required string WebhookSecret { get; init; }
}
