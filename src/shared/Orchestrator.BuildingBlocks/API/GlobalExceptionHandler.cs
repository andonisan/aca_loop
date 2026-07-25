using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Orchestrator.BuildingBlocks.Diagnostics;

namespace Orchestrator.BuildingBlocks.API;

/// <summary>Translates unhandled exceptions to RFC 7807 ProblemDetails, never leaking a stack trace.</summary>
sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        var isMalformedBody =
            exception is BadHttpRequestException or System.Text.Json.JsonException;

        var problemDetails = new ProblemDetails
        {
            Status = isMalformedBody
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status500InternalServerError,
            Title = isMalformedBody
                ? "The request body could not be parsed."
                : "An unexpected error occurred.",
        };

        if (isMalformedBody)
        {
            ApiLog.MalformedRequestBody(logger, exception);
        }
        else
        {
            ApiLog.UnhandledException(logger, exception);
        }

        httpContext.Response.StatusCode = problemDetails.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
