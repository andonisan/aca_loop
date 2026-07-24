using Microsoft.Extensions.DependencyInjection;

namespace Orchestrator.BuildingBlocks.CQS;

public static class CqsServiceCollectionExtensions
{
    /// <summary>Registers <see cref="ISender"/>. Call once in the host, before or after module registration.</summary>
    public static IServiceCollection AddVsaCqsArchitecture(this IServiceCollection services)
    {
        services.AddScoped<ISender, Sender>();
        return services;
    }
}
