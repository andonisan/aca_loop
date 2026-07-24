namespace Orchestrator.Modules.Backlog.Contracts;

/// <summary>A supported backlog vendor. The string form (lowercase, kebab where needed) is what
/// travels on the wire in <see cref="CanonicalEvent"/>/<see cref="CanonicalMessage"/> and in the
/// mapping configuration — never a vendor-internal enum ordinal.</summary>
public enum Vendor
{
    GitHub,
    GitLab,
    AzureDevOps,
}
