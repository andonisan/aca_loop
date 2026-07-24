using ErrorOr;
using FluentValidation;

namespace Orchestrator.BuildingBlocks.CQS.Decorators;

sealed class ValidationQueryDecorator<TQuery, TResponse>(
    IAppQueryHandler<TQuery, TResponse> inner,
    IEnumerable<IValidator<TQuery>> validators
) : IAppQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    public async Task<TResponse> Handle(
        TQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var validatorList = validators as IValidator<TQuery>[] ?? [.. validators];
        if (validatorList.Length == 0)
        {
            return await inner.Handle(query, cancellationToken);
        }

        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validatorList)
        {
            var result = await validator.ValidateAsync(query, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count == 0)
        {
            return await inner.Handle(query, cancellationToken);
        }

        var errors = failures
            .Select(f => Error.Validation(f.PropertyName, f.ErrorMessage))
            .ToList();

        return ErrorOrFactory.FromErrors<TResponse>(errors);
    }
}
