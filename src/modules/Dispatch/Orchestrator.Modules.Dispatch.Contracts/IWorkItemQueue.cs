using Orchestrator.Modules.Backlog.Contracts;

namespace Orchestrator.Modules.Dispatch.Contracts;

public interface IWorkItemQueue
{
    Task EnqueueAsync(CanonicalMessage message, CancellationToken cancellationToken = default);
}
