using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Orchestrator.BuildingBlocks.CQS;

public static class CqsRegistration
{
    /// <summary>Scans <paramref name="assembly"/> for CQS handlers and FluentValidation validators and registers them.</summary>
    public static IServiceCollection AddCqsHandlers(
        this IServiceCollection services,
        Assembly assembly
    )
    {
        services.Scan(scan =>
            scan.FromAssemblies(assembly)
                // publicOnly: false — handlers are internal sealed by convention (see CQS001).
                .AddClasses(
                    classes =>
                        classes.AssignableToAny(
                            typeof(IAppCommandHandler<,>),
                            typeof(IAppQueryHandler<,>)
                        ),
                    publicOnly: false
                )
                .AsImplementedInterfaces()
                .WithScopedLifetime()
        );

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        return services;
    }
}
