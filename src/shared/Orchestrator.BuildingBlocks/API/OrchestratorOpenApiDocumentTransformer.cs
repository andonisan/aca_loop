using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Orchestrator.BuildingBlocks.API;

sealed class OrchestratorOpenApiDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        document.Info.Title = "Agentic Backlog Orchestrator — Receiver API";
        document.Info.Version = "v1";
        document.Info.Description = "Vendor webhook intake and dispatch.";

        return Task.CompletedTask;
    }
}
