using Orchestrator.BuildingBlocks.Diagnostics;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Orchestrator.BuildingBlocks.CQS.Decorators;

sealed class LoggingQueryDecorator<TQuery, TResponse>(
    IAppQueryHandler<TQuery, TResponse> inner,
    ILogger<LoggingQueryDecorator<TQuery, TResponse>> logger
) : IAppQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    public async Task<TResponse> Handle(
        TQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var requestName = typeof(TQuery).Name;
        CqsLog.HandlingRequest(logger, requestName);

        try
        {
            var response = await inner.Handle(query, cancellationToken);

            if (response is IErrorOr { IsError: true } errorOr)
            {
                CqsLog.RequestReturnedFunctionalError(
                    logger,
                    requestName,
                    string.Join(", ", errorOr.Errors!.Select(e => e.Code))
                );
            }
            else
            {
                CqsLog.RequestSucceeded(logger, requestName);
            }

            return response;
        }
        catch (Exception exception)
        {
            CqsLog.RequestFailed(logger, exception, requestName);
            throw;
        }
    }
}
