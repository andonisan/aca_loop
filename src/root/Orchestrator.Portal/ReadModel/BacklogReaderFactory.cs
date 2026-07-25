using Microsoft.Extensions.Options;
using Orchestrator.Modules.AzureDevOps;
using Orchestrator.Modules.Backlog.Contracts;
using Orchestrator.Modules.GitHub;
using Orchestrator.Modules.GitLab;
using Orchestrator.Modules.Jira;
using Orchestrator.Portal.Registry;

namespace Orchestrator.Portal.ReadModel;

/// <summary>
/// Builds a read-only vendor client for one stack.
/// <para>
/// A factory rather than DI-registered readers, because everything a reader needs is per stack, not per
/// vendor: the instance address (two clients can be on different GitLab or Jira servers), the read-scoped
/// credential, and for Jira the Cloud/Data Center split. The receiver and agent can register a vendor
/// client as a singleton because they serve exactly one target; the portal cannot.
/// </para>
/// </summary>
sealed class BacklogReaderFactory(
    IHttpClientFactory httpClientFactory,
    IReadCredentialResolver credentials
)
{
    public IBacklogReader For(RegisteredStack stack)
    {
        var vendor = stack.Vendor;
        var client = httpClientFactory.CreateClient("vendor-read");
        client.BaseAddress = vendor.BaseUrl;

        // Every reader receives the credential the same way: resolved per call from this stack's Key Vault
        // secret name. No reader can mint or escalate one, which is what makes "the portal cannot write"
        // structural rather than a deployment promise (design D9).
        Task<string> Token(string _, CancellationToken cancellationToken) =>
            credentials.ResolveAsync(stack, cancellationToken);

        return vendor.Vendor switch
        {
            Vendor.GitHub => new GitHubBacklogReader(client) { ReadTokenFor = Token },
            Vendor.GitLab => BuildGitLab(client, Token),
            Vendor.AzureDevOps => new AzureDevOpsBacklogReader(client) { ReadTokenFor = Token },
            Vendor.Jira => BuildJira(stack, client, Token),
            _ => throw new InvalidOperationException(
                $"no read-only client for vendor '{vendor.Vendor}'"
            ),
        };
    }

    /// <summary>GitLab's API lives under /api/v4/ rather than at the instance root.</summary>
    static GitLabBacklogReader BuildGitLab(
        HttpClient client,
        Func<string, CancellationToken, Task<string>> token
    )
    {
        client.BaseAddress = new Uri($"{client.BaseAddress!.ToString().TrimEnd('/')}/api/v4/");
        return new GitLabBacklogReader(client) { ReadTokenFor = token };
    }

    /// <summary>Jira's reader still takes options, because deployment drives REST version and auth scheme.
    /// They are built per stack rather than bound from configuration, since the portal's stacks can be on
    /// different Jira products.</summary>
    static JiraBacklogReader BuildJira(
        RegisteredStack stack,
        HttpClient client,
        Func<string, CancellationToken, Task<string>> token
    ) =>
        new(
            client,
            Options.Create(
                new JiraOptions
                {
                    BaseUrl = stack.Vendor.BaseUrl.ToString(),
                    Deployment =
                        stack.Vendor.Deployment == "DataCenter"
                            ? JiraDeployment.DataCenter
                            : JiraDeployment.Cloud,
                    // The account email is the Basic username on Cloud; it travels with the credential,
                    // so it is read from the same place rather than configured per vendor.
                    AccountEmail = stack.Vendor.Project,
                    // Never read: the reader takes its token through ReadTokenFor. Present only because
                    // JiraOptions requires it, and deliberately not the real one -- if this ever *is*
                    // read, it must fail rather than silently authenticate with something unexpected.
                    ApiToken = "",
                    WebhookSecret = "",
                    BotAccountId = "",
                }
            )
        )
        {
            ReadTokenFor = token,
        };
}
