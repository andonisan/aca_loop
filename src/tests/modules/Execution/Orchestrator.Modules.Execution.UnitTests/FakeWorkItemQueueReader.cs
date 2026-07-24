using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.Execution.UnitTests;

sealed class FakeWorkItemQueueReader : IWorkItemQueueReader
{
    ReceivedWorkItem? _pending;

    public List<ReceivedWorkItem> Deleted { get; } = [];

    public static FakeWorkItemQueueReader WithMessage(
        CanonicalMessage payload,
        long dequeueCount = 1
    )
    {
        var reader = new FakeWorkItemQueueReader();
        reader._pending = new ReceivedWorkItem("msg-1", "pop-1", dequeueCount, payload);
        return reader;
    }

    public Task<ReceivedWorkItem?> ReceiveAsync(
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default
    )
    {
        var next = _pending;
        _pending = null;
        return Task.FromResult(next);
    }

    public Task DeleteAsync(ReceivedWorkItem item, CancellationToken cancellationToken = default)
    {
        Deleted.Add(item);
        return Task.CompletedTask;
    }
}
