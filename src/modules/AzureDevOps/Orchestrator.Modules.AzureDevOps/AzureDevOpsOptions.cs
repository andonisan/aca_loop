namespace Orchestrator.Modules.AzureDevOps;

/// <summary>
/// Configuration for the Azure DevOps vendor adapter. Bound from the <c>Vendors:AzureDevOps</c>
/// configuration section; <see cref="PersonalAccessToken"/> and the webhook basic-auth password are
/// expected to be sourced from Key Vault at the host level, never a plain appsettings value.
/// <see cref="Vendor.AzureDevOps"/>'s "repo" coordinate (<c>CanonicalEvent.Repo</c> /
/// <c>CanonicalMessage.Repo</c>) is the ADO **project** name — Boards work items are project-scoped,
/// not repo-scoped. The git repo cloned for an execution is assumed to share that same name (the
/// common single-repo-per-project convention); a project whose primary repo is named differently
/// needs the not-yet-built per-target repo override this MVP doesn't have — see design.md D1/5.1.
/// </summary>
public sealed class AzureDevOpsOptions
{
    public const string SectionName = "Vendors:AzureDevOps";

    /// <summary>Organization name, e.g. "contoso" for https://dev.azure.com/contoso.</summary>
    public required string Organization { get; init; }

    /// <summary>Display name or unique name (email) of this automation's own identity, as it
    /// appears in <c>revisedBy</c> on a work item update. Events from this identity are dropped so
    /// the loop can't trigger itself.</summary>
    public required string BotIdentity { get; init; }

    /// <summary>Personal Access Token, scoped to Work Items (Read &amp; write) and Code (Read) on
    /// the target projects. Azure DevOps has no App-equivalent JIT token minting (design.md D6).</summary>
    public required string PersonalAccessToken { get; init; }

    /// <summary>Basic-auth username Azure DevOps sends on the configured Service Hook subscription.</summary>
    public required string WebhookUsername { get; init; }

    /// <summary>Basic-auth password Azure DevOps sends on the configured Service Hook subscription.</summary>
    public required string WebhookPassword { get; init; }
}
