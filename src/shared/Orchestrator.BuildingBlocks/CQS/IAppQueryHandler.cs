namespace Orchestrator.BuildingBlocks.CQS;

public interface IAppQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<TResponse> Handle(TQuery query, CancellationToken cancellationToken = default);
}
