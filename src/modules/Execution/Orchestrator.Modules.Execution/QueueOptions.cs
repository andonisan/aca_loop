namespace Orchestrator.Modules.Execution;

/// <summary>Storage Queue connection for the Agent's dequeue side. See Dispatch's identically-named
/// options for the rationale — each side owns its own small queue client, mirroring v1's two
/// independent <c>queue.js</c> files (webhook-receiver enqueues, agent dequeues).</summary>
public sealed class QueueOptions
{
    public const string SectionName = "Queue";

    public string? ConnectionString { get; init; }
    public string? AccountName { get; init; }
    public required string QueueName { get; init; }
}
