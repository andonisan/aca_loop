namespace Orchestrator.BuildingBlocks.CQS;

/// <summary>Dispatches commands/queries to their handler through the Logging → Validation pipeline.</summary>
public interface ISender
{
    Task<TResponse> Send<TResponse>(
        ICommand<TResponse> command,
        CancellationToken cancellationToken = default
    );

    Task<TResponse> Send<TResponse>(
        IQuery<TResponse> query,
        CancellationToken cancellationToken = default
    );
}
