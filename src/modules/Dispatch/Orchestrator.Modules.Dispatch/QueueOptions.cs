namespace Orchestrator.Modules.Dispatch;

/// <summary>
/// Storage Queue connection for the AI-pod dispatch pipeline. <see cref="ConnectionString"/> is used
/// for local dev against Azurite (via Aspire); in Azure, leave it unset and provide
/// <see cref="AccountName"/> — the queue is then addressed via its HTTPS endpoint with
/// <c>DefaultAzureCredential</c> (managed identity), matching the v1 stack (README.md).
/// </summary>
public sealed class QueueOptions
{
    public const string SectionName = "Queue";

    public string? ConnectionString { get; init; }
    public string? AccountName { get; init; }
    public required string QueueName { get; init; }
}
