using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orchestrator.Modules.ConfigStore.Infrastructure.Persistence;

namespace Orchestrator.Modules.ConfigStore;

/// <summary>
/// The one way to bring this module's schema up to date.
/// <para>
/// Deliberately <b>not</b> part of <c>ConfigStoreModule.InitializeAsync</c>. Every host that composes this
/// module runs <c>InitializeAsync</c>, and receivers compose it too — so putting migration there would
/// have hundreds of readers racing to migrate a schema they do not own. Making it a separate, explicit
/// call means "who migrates" is a decision each host states out loud, and exactly one of them says yes
/// (design D8).
/// </para>
/// <para>
/// It lives here rather than in the Portal because <see cref="ConfigStoreDbContext"/> is internal to this
/// module and stays that way; the host gets a verb, not the model.
/// </para>
/// </summary>
public static class ConfigStoreSchema
{
    public static async Task MigrateConfigStoreAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default
    )
    {
        // A scope of its own: the context is scoped, and at startup there is no request scope to borrow.
        await using var scope = serviceProvider.CreateAsyncScope();

        var database = scope.ServiceProvider.GetRequiredService<ConfigStoreDbContext>().Database;

        await database.MigrateAsync(cancellationToken);
    }
}
