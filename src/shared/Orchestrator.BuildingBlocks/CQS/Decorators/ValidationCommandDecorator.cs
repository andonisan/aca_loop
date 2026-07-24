using ErrorOr;
using FluentValidation;

namespace Orchestrator.BuildingBlocks.CQS.Decorators;

sealed class ValidationCommandDecorator<TCommand, TResponse>(
    IAppCommandHandler<TCommand, TResponse> inner,
    IEnumerable<IValidator<TCommand>> validators
) : IAppCommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<TResponse> Handle(
        TCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var validatorList = validators as IValidator<TCommand>[] ?? [.. validators];
        if (validatorList.Length == 0)
        {
            return await inner.Handle(command, cancellationToken);
        }

        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validatorList)
        {
            var result = await validator.ValidateAsync(command, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count == 0)
        {
            return await inner.Handle(command, cancellationToken);
        }

        var errors = failures
            .Select(f => Error.Validation(f.PropertyName, f.ErrorMessage))
            .ToList();

        return ErrorOrFactory.FromErrors<TResponse>(errors);
    }
}
