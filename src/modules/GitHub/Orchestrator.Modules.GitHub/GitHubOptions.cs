namespace Orchestrator.Modules.GitHub;

/// <summary>Configuration for the GitHub vendor adapter. Bound from the <c>Vendors:GitHub</c>
/// configuration section; secret values (<see cref="PrivateKeyPem"/>, <see cref="WebhookSecret"/>)
/// are expected to be sourced from Key Vault at the host level, never a plain appsettings value.</summary>
public sealed class GitHubOptions
{
    public const string SectionName = "Vendors:GitHub";

    public required string AppId { get; init; }
    public required string InstallationId { get; init; }
    public required string BotLogin { get; init; }
    public required string PrivateKeyPem { get; init; }
    public required string WebhookSecret { get; init; }
}
