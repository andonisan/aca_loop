using Microsoft.EntityFrameworkCore;
using Orchestrator.Modules.ConfigStore.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Orchestrator.Modules.ConfigStore.UnitTests;

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
    readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(
        "postgres:18-alpine"
    ).Build();

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
