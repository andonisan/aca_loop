using Orchestrator.BuildingBlocks.Modules;
using Orchestrator.Constants;
using Orchestrator.Modules.Backlog.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Orchestrator.Modules.Dispatch;

/// <summary>
/// The single vendor-agnostic webhook intake endpoint. Verifies the payload through the matching
/// <see cref="IVendorAdapter"/>, resolves it against the state-action-config mapping, and enqueues
/// the canonical message — mirroring v1's <c>webhook-receiver/src/index.js</c> but vendor-blind and
/// mapping-driven instead of a hardcoded gate-label set.
/// </summary>
public sealed class WebhookUseCase : IUseCase
{
    public static void AddRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/webhooks/{vendor}", HandleAsync).AllowAnonymous();
    }

    /// <summary>Internal rather than private so the unit tests can drive the handler directly
    /// (<c>InternalsVisibleTo</c>) instead of standing up a host to assert on one enqueue path.</summary>
    internal static async Task<IResult> HandleAsync(
        string vendor,
        HttpContext httpContext,
        IEnumerable<IVendorAdapter> adapters,
        IMappingProvider mappingProvider,
        IWorkItemQueue queue,
        ILogger<WebhookUseCase> logger,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseVendor(vendor, out var parsedVendor))
        {
            return Results.NotFound();
        }

        var adapter = adapters.FirstOrDefault(a => a.Vendor == parsedVendor);
        if (adapter is null)
        {
            return Results.NotFound();
        }

        using var bodyStream = new MemoryStream();
        await httpContext.Request.Body.CopyToAsync(bodyStream, cancellationToken);
        var rawBody = bodyStream.ToArray();

        var headers = httpContext
            .Request.Headers.ToDictionary(
                h => h.Key,
                h => h.Value.ToString(),
                StringComparer.OrdinalIgnoreCase
            );

        if (!adapter.VerifyWebhook(rawBody, headers))
        {
            return Results.Unauthorized();
        }

        var canonicalEvent = adapter.ParseEvent(rawBody, headers);
        if (canonicalEvent is null)
        {
            return Results.Accepted();
        }

        if (adapter.IsAutomationActor(canonicalEvent.Actor))
        {
            return Results.Accepted();
        }

        if (
            !mappingProvider.TryResolve(
                canonicalEvent.Vendor,
                canonicalEvent.Project,
                canonicalEvent.Repo,
                canonicalEvent.NativeState,
                out var stateMapping
            )
        )
        {
            // Unknown target, or a state not declared for this target's mapping -- both are "not a
            // gate state" per the state-action-config spec, not an error.
            return Results.Accepted();
        }

        var message = new CanonicalMessage(
            canonicalEvent.Vendor,
            canonicalEvent.Project,
            canonicalEvent.Repo,
            canonicalEvent.ItemId,
            canonicalEvent.NativeState,
            stateMapping!.Action,
            stateMapping.Runtime.ToString(),
            stateMapping.OnFailureState
        );

        await queue.EnqueueAsync(message, cancellationToken);

        // After the enqueue, not before: a row in the read model must mean the work was really
        // handed off, never merely intended (portal-read-model spec).
        DispatchLog.Enqueued(
            logger,
            LifecycleEvents.Enqueued,
            LifecycleEvents.SchemaVersion,
            message.Vendor.ToString(),
            message.Project,
            message.Repo,
            message.ItemId,
            message.State,
            message.Action,
            message.Runtime
        );

        return Results.Accepted();
    }

    static bool TryParseVendor(string value, out Vendor vendor)
    {
        switch (value.ToLowerInvariant())
        {
            case "github":
                vendor = Vendor.GitHub;
                return true;
            case "gitlab":
                vendor = Vendor.GitLab;
                return true;
            case "azure-devops":
                vendor = Vendor.AzureDevOps;
                return true;
            default:
                vendor = default;
                return false;
        }
    }
}
