using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Orchestrator.BuildingBlocks.API;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        services.AddHttpContextAccessor();

        services.AddOpenApi(options =>
        {
            options.CreateSchemaReferenceId = jsonTypeInfo => jsonTypeInfo.Type.Name;
            options.AddDocumentTransformer<OrchestratorOpenApiDocumentTransformer>();
        });

        return services;
    }

    public static WebApplication UseApiServices(this WebApplication app)
    {
        app.UseExceptionHandler();
        return app;
    }
}
