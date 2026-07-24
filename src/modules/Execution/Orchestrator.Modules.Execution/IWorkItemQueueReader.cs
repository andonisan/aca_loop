namespace Orchestrator.Modules.Execution;

public interface IWorkItemQueueReader
{
    /// <summary>Dequeues at most one message, hidden for <paramref name="visibilityTimeout"/> so a
    /// crashed execution's message reappears for retry (capped by the poison-message guard). Returns
    /// null if the queue is empty — expected under KEDA overscale.</summary>
    Task<ReceivedWorkItem?> ReceiveAsync(
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default
    );

    Task DeleteAsync(ReceivedWorkItem item, CancellationToken cancellationToken = default);
}
