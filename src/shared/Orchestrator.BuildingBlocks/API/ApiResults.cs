using ErrorOr;
using Microsoft.AspNetCore.Http;

namespace Orchestrator.BuildingBlocks.API;

/// <summary>Maps <c>ErrorOr</c> failures to RFC 7807 <c>ProblemDetails</c> results.</summary>
public static class ApiResults
{
    public static IResult Problem(List<Error> errors)
    {
        if (errors.Count == 0)
        {
            return Results.Problem();
        }

        if (errors.All(e => e.Type == ErrorType.Validation))
        {
            return ValidationProblem(errors);
        }

        var firstError = errors[0];
        var statusCode = firstError.Type switch
        {
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError,
        };

        return Results.Problem(
            statusCode: statusCode,
            title: firstError.Description,
            extensions: new Dictionary<string, object?> { ["errorCode"] = firstError.Code }
        );
    }

    static IResult ValidationProblem(List<Error> errors)
    {
        var errorsDictionary = errors
            .GroupBy(e => e.Code)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

        return Results.ValidationProblem(errorsDictionary);
    }
}
