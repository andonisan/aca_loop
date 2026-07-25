using ErrorOr;
using Microsoft.Extensions.Logging;
using Orchestrator.BuildingBlocks.Diagnostics;

namespace Orchestrator.BuildingBlocks.CQS.Decorators;

sealed class LoggingCommandDecorator<TCommand, TResponse>(
    IAppCommandHandler<TCommand, TResponse> inner,
    ILogger<LoggingCommandDecorator<TCommand, TResponse>> logger
) : IAppCommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<TResponse> Handle(
        TCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var requestName = typeof(TCommand).Name;
        CqsLog.HandlingRequest(logger, requestName);

        try
        {
            var response = await inner.Handle(command, cancellationToken);

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
