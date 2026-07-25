using Microsoft.Extensions.DependencyInjection;
using Orchestrator.BuildingBlocks.CQS.Decorators;

namespace Orchestrator.BuildingBlocks.CQS;

/// <summary>
/// Resolves the handler for a request and wraps it with the Logging → Validation decorators,
/// composed per-call via <see cref="ActivatorUtilities"/> so the pipeline needs no DI
/// registration order relative to module handler registration.
/// </summary>
sealed class Sender(IServiceProvider serviceProvider) : ISender
{
    static readonly string HandleMethodName = nameof(
        IAppCommandHandler<ICommand<object>, object>.Handle
    );

    public Task<TResponse> Send<TResponse>(
        ICommand<TResponse> command,
        CancellationToken cancellationToken = default
    )
    {
        var commandType = command.GetType();
        var handlerType = typeof(IAppCommandHandler<,>).MakeGenericType(
            commandType,
            typeof(TResponse)
        );

        object pipeline = serviceProvider.GetRequiredService(handlerType);
        pipeline = ActivatorUtilities.CreateInstance(
            serviceProvider,
            typeof(ValidationCommandDecorator<,>).MakeGenericType(commandType, typeof(TResponse)),
            pipeline
        );
        pipeline = ActivatorUtilities.CreateInstance(
            serviceProvider,
            typeof(LoggingCommandDecorator<,>).MakeGenericType(commandType, typeof(TResponse)),
            pipeline
        );

        return Invoke<TResponse>(handlerType, pipeline, command, cancellationToken);
    }

    public Task<TResponse> Send<TResponse>(
        IQuery<TResponse> query,
        CancellationToken cancellationToken = default
    )
    {
        var queryType = query.GetType();
        var handlerType = typeof(IAppQueryHandler<,>).MakeGenericType(queryType, typeof(TResponse));

        object pipeline = serviceProvider.GetRequiredService(handlerType);
        pipeline = ActivatorUtilities.CreateInstance(
            serviceProvider,
            typeof(ValidationQueryDecorator<,>).MakeGenericType(queryType, typeof(TResponse)),
            pipeline
        );
        pipeline = ActivatorUtilities.CreateInstance(
            serviceProvider,
            typeof(LoggingQueryDecorator<,>).MakeGenericType(queryType, typeof(TResponse)),
            pipeline
        );

        return Invoke<TResponse>(handlerType, pipeline, query, cancellationToken);
    }

    static Task<TResponse> Invoke<TResponse>(
        Type handlerType,
        object handler,
        object request,
        CancellationToken cancellationToken
    )
    {
        var method = handlerType.GetMethod(HandleMethodName)!;
        return (Task<TResponse>)method.Invoke(handler, [request, cancellationToken])!;
    }
}
