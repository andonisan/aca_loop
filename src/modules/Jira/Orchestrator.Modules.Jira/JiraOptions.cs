namespace Orchestrator.Modules.Jira;

/// <summary>Which Jira product a target runs on. Selects REST version, comment body format, and auth
/// scheme together, because those three diverge in lockstep and combinations like "Data Center with
/// Atlassian Document Format" do not exist (design.md D11).</summary>
public enum JiraDeployment
{
    Cloud,
    DataCenter,
}

/// <summary>Configuration for the Jira vendor adapter. Bound from the <c>Vendors:Jira</c> section;
/// <see cref="ApiToken"/> and <see cref="WebhookSecret"/> are sourced from Key Vault at the host
/// level, never a plain appsettings value.</summary>
public sealed class JiraOptions
{
    public const string SectionName = "Vendors:Jira";

    /// <summary>Instance URL, e.g. "https://acme.atlassian.net" (Cloud) or a self-hosted host.</summary>
    public required string BaseUrl { get; init; }

    /// <summary>
    /// Drives <see cref="ApiVersion"/> and the comment body format. Deliberately one setting rather
    /// than three: an operator cannot select a combination that does not exist, and a wrong value
    /// fails loudly on the first API call instead of only on the failure path.
    /// </summary>
    public JiraDeployment Deployment { get; init; } = JiraDeployment.Cloud;

    /// <summary>Account email on Cloud (used as the Basic username alongside <see cref="ApiToken"/>).
    /// Ignored on Data Center, where the token is a bearer personal access token.</summary>
    public string? AccountEmail { get; init; }

    /// <summary>Cloud: an API token, sent as the Basic password with <see cref="AccountEmail"/>.
    /// Data Center: a personal access token, sent as a bearer token. Jira offers no App-equivalent JIT
    /// minting, so this is a longer-lived secret — the same documented asymmetry as GitLab and Azure
    /// DevOps (agentic-backlog-orchestrator design.md D6).</summary>
    public required string ApiToken { get; init; }

    /// <summary>Shared secret for webhook HMAC verification. Atlassian treats the webhook secret as
    /// optional at registration; this adapter does not — an unsigned request is rejected rather than
    /// treated as "verification not configured" (design.md D2).</summary>
    public required string WebhookSecret { get; init; }

    /// <summary>The orchestrator's own Jira account id, so its own transitions and comments never
    /// re-dispatch. An account id rather than a display name: display names are mutable and not
    /// unique, and Jira's GDPR changes removed email from most webhook payloads.</summary>
    public required string BotAccountId { get; init; }

    /// <summary>Retry attempts after the first, for rate-limited and transient-server responses.
    /// Four (five attempts total) is Atlassian's own guidance.</summary>
    public int MaxRetries { get; init; } = 4;

    /// <summary>Base for the exponential backoff when a response carries no <c>Retry-After</c>: the
    /// nth retry waits <c>base * 2^n</c> plus jitter. Configurable mainly so tests do not have to
    /// spend real seconds proving the policy.</summary>
    public int RetryBaseDelayMilliseconds { get; init; } = 1000;

    /// <summary>Data Center has no <c>/rest/api/3</c> at all; v3 and its Atlassian Document Format
    /// bodies are Cloud-only.</summary>
    public string ApiVersion => Deployment == JiraDeployment.Cloud ? "3" : "2";
}
