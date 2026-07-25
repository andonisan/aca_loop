using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orchestrator.Constants;
using Orchestrator.Modules.ConfigStore.Contracts;
using Orchestrator.Modules.ConfigStore.Infrastructure.Persistence;

namespace Orchestrator.Modules.ConfigStore;

/// <summary>
/// Registration, callable directly rather than only through module discovery.
/// <para>
/// The two hosts that need this compose differently. The Receiver calls <c>RegisterAppModules</c> and
/// picks up every <c>Orchestrator.Modules.*</c> assembly it references, so <see cref="ConfigStoreModule"/>
/// finds it automatically. The Portal composes explicitly — it references the vendor modules for their
/// read-only clients but must not register their adapters, whose options it does not configure — so it
/// calls this instead.
/// </para>
/// <para>
/// Both paths land here, so there is one definition of how this module is wired regardless of which host
/// is asking.
/// </para>
/// </summary>
public static class ConfigStoreServiceCollectionExtensions
{
    public static IServiceCollection AddConfigStore(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString = configuration.GetConnectionString(Components.Database);

        services.AddDbContext<ConfigStoreDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                // The history table lives in this module's schema too, so two modules' migrations never
                // share a ledger.
                npgsql =>
                    npgsql.MigrationsHistoryTable(
                        "__EFMigrationsHistory",
                        ConfigStoreDbContext.Schema
                    )
            )
        );

        services.AddScoped<IConfigStore, PostgresConfigStore>();

        return services;
    }
}
