using Microsoft.EntityFrameworkCore;
using Orchestrator.Modules.ConfigStore.Features.Stacks.Domain;

namespace Orchestrator.Modules.ConfigStore.Infrastructure.Persistence;

/// <summary>
/// The config store's own schema.
/// <para>
/// <c>internal sealed</c>, following ds-connect: no host references it and no other module can. That is
/// precisely what makes going without a repository layer safe — handlers take this directly, and the
/// assembly boundary is the protection a repository interface would otherwise be pretending to provide.
/// </para>
/// <para>
/// One schema per module against one physical database. Isolation is by schema and there are no
/// cross-schema queries (design D9).
/// </para>
/// </summary>
sealed class ConfigStoreDbContext(DbContextOptions<ConfigStoreDbContext> options)
    : DbContext(options)
{
    public const string Schema = "configstore";

    public DbSet<Stack> Stacks => Set<Stack>();
    public DbSet<MappingState> MappingStates => Set<MappingState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // One of the three places the schema is declared, in concert: here, on the migrations history
        // table at registration, and via EnsureSchema in the first migration.
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ConfigStoreDbContext).Assembly);
    }
}
