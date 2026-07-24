using System.Reflection;
using ErrorOr;

namespace Orchestrator.BuildingBlocks.CQS;

/// <summary>
/// Builds a <typeparamref name="TResponse"/> (expected to be <c>ErrorOr&lt;T&gt;</c>) from a list of
/// errors via its implicit <c>List&lt;Error&gt;</c> conversion operator, resolved through reflection
/// since the decorator pipeline only knows <typeparamref name="TResponse"/> generically.
/// </summary>
static class ErrorOrFactory
{
    public static TResponse FromErrors<TResponse>(List<Error> errors)
    {
        var conversion =
            typeof(TResponse).GetMethod(
                "op_Implicit",
                BindingFlags.Public | BindingFlags.Static,
                [typeof(List<Error>)]
            )
            ?? throw new InvalidOperationException(
                $"{typeof(TResponse).Name} does not support implicit conversion from List<Error>. CQS responses must be ErrorOr<T>."
            );

        return (TResponse)conversion.Invoke(null, [errors])!;
    }
}
