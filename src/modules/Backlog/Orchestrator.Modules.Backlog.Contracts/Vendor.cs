namespace Orchestrator.Modules.Backlog.Contracts;

/// <summary>A supported backlog vendor. The string form (lowercase, kebab where needed) is what
/// travels on the wire in <see cref="CanonicalEvent"/>/<see cref="CanonicalMessage"/> and in the
/// mapping configuration — never a vendor-internal enum ordinal.</summary>
public enum Vendor
{
    GitHub,
    GitLab,
    AzureDevOps,

    /// <summary>A tracker only — Jira hosts no git, so a Jira target pairs with one of the others as
    /// its code host. The value exists ahead of the adapter (jira-adapter group 3) so the mapping
    /// schema can express and validate split tracker/code-host targets; until the adapter lands,
    /// <c>/webhooks/jira</c> is unroutable and a Jira message has no adapter to execute it.</summary>
    Jira,
}
