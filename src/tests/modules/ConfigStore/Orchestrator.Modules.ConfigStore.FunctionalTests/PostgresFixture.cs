using Microsoft.EntityFrameworkCore;
using Orchestrator.Modules.ConfigStore.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Orchestrator.Modules.ConfigStore.FunctionalTests;

/// <summary>
/// A real Postgres, with this module's real migrations applied. Not an in-memory provider and not SQLite:
/// the things worth testing here — the schema actually being created, the composite key, the cascade, the
/// enum-as-string conversion — are all provider behaviour, and a substitute provider would assert that the
/// substitute behaves, which is not the question.
/// <para>
/// Applying migrations here rather than <c>EnsureCreated</c> is deliberate too: it means the migration is
/// what gets tested, so a migration that drifts from the model fails a test rather than a deployment.
/// </para>
/// <para>
/// <b>Needs <c>DOCKER_HOST</c> and <c>TESTCONTAINERS_RYUK_DISABLED=true</c> on this machine</b> — see
/// <c>AzuriteFixture</c> in the Portal functional tests for why; both failures present as a hang.
/// </para>
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>
    /// The conventional Docker Hub name, overridable by <c>ORCHESTRATOR_POSTGRES_IMAGE</c>.
    /// <para>
    /// Overridable deliberately. ds-connect pins its images as private <c>const</c> fields, which means a
    /// machine or a runner that cannot reach Docker Hub has no way in short of editing the shared base —
    /// and Docker Hub is unreachable from at least one machine this repo is developed on, where MCR is
    /// fine. The default stays conventional so nothing changes for a normal network; the override exists
    /// so a registry mirror can be pointed at without touching code.
    /// </para>
    /// </summary>
    static string Image =>
        Environment.GetEnvironmentVariable("ORCHESTRATOR_POSTGRES_IMAGE") is { Length: > 0 } image
            ? image
            : "postgres:18-alpine";

    readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(Image).Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var dbContext = NewContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    /// <summary>A fresh context per use, as scoped registration would give at runtime — a shared one would
    /// let one test's change tracker leak into the next.</summary>
    internal ConfigStoreDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<ConfigStoreDbContext>()
                .UseNpgsql(
                    ConnectionString,
                    npgsql =>
                        npgsql.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            ConfigStoreDbContext.Schema
                        )
                )
                .Options
        );
}
