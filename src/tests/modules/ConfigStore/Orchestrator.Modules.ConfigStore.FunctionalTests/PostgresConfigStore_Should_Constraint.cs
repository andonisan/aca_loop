using Microsoft.EntityFrameworkCore;
using Orchestrator.Modules.Backlog.Contracts;
using Orchestrator.Modules.ConfigStore.Infrastructure.Persistence;
using Shouldly;

namespace Orchestrator.Modules.ConfigStore.FunctionalTests;

/// <summary>
/// The read path and the schema, against a real Postgres with the real migration applied. What is being
/// tested is mostly not C# — it is whether the schema this module declares behaves as the design assumes.
/// </summary>
public sealed class PostgresConfigStore_Should_Constraint(PostgresFixture postgres)
    : IClassFixture<PostgresFixture>
{
    /// <summary>Raw SQL rather than the entity types, because the entities are internal to the module and
    /// because inserting through the model would test the model against itself. This inserts the way an
    /// importer or a migration script would — one of the paths design D3 explicitly says exists.</summary>
    async Task InsertStackAsync(string id, params (string State, string Action)[] states)
    {
        await using var db = postgres.NewContext();

        await db.Database.ExecuteSqlRawAsync(
            """
            insert into configstore.stacks
              ("Id","DisplayName","SubscriptionId","ResourceGroup","QueueServiceUri","QueueName",
               "JobName","LogAnalyticsWorkspaceId","Vendor","VendorProject","VendorRepo",
               "ReadCredentialSecretName","VendorBaseUrl","VendorDeployment","LastWrittenBy","LastWrittenAt")
            values ({0},'Display','sub','rg','https://acct.queue.core.windows.net','ai-jobs','agent-job',
                    'ws','GitHub',null,'acme/widgets','Portal--Read','https://api.github.com',null,
                    'someone@acme.com', now())
            """,
            id
        );

        foreach (var (state, action) in states)
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                insert into configstore.mapping_states
                  ("StackId","NativeState","Action","Runtime","OnSuccessState","OnFailureState",
                   "CodeHost","CodeRepo","LastWrittenBy","LastWrittenAt")
                values ({0},{1},{2},'Claude',null,'status:blocked','GitHub','acme/widgets',
                        'someone@acme.com', now())
                """,
                id,
                state,
                action
            );
        }
    }

    [Fact]
    public async Task Migration_Should_CreateTheModulesOwnSchema()
    {
        await using var db = postgres.NewContext();

        // The schema is the module's isolation boundary; if the migration silently created these in
        // public, two modules would eventually collide on a table name.
        // The "Value" alias is not cosmetic: SqlQuery<T> wraps this as `select s."Value" from (...) s`
        // for scalar results, so a column by that exact name has to exist or the wrapper fails to bind.
        var schema = await db
            .Database.SqlQuery<string>(
                $"""select table_schema as "Value" from information_schema.tables where table_name = 'stacks'"""
            )
            .SingleAsync();

        schema.ShouldBe("configstore");
    }

    [Fact]
    public async Task Store_Should_ReturnAStackWithItsStates()
    {
        await InsertStackAsync(
            "read-one",
            ("status:ready-for-proposal", "/ds:propose"),
            ("status:ready-for-implementation", "/ds:implement")
        );

        await using var db = postgres.NewContext();
        var stack = await new PostgresConfigStore(db).GetStackAsync("read-one");

        stack.ShouldNotBeNull();
        stack.Vendor.Vendor.ShouldBe(Vendor.GitHub);
        stack.Vendor.BaseUrl.ToString().ShouldBe("https://api.github.com/");
        stack.States.Count.ShouldBe(2);
        stack.States.ShouldContain(s => s.Action == "/ds:propose");
    }

    [Fact]
    public async Task Store_Should_ReturnNullForAnUnregisteredStack()
    {
        await using var db = postgres.NewContext();

        // Null, not an empty stack: "not registered" and "registered with nothing mapped" are different
        // answers and the caller has to be able to tell them apart.
        (await new PostgresConfigStore(db).GetStackAsync("never-existed")).ShouldBeNull();
    }

    [Fact]
    public async Task Schema_Should_RejectTheSameStateTwiceForOneStack()
    {
        await InsertStackAsync("dup-state", ("status:ready-for-proposal", "/ds:propose"));

        // The mapping format's states are a keyed map, so a duplicate is unrepresentable there. The
        // database has to enforce the same thing, because writes can arrive by paths that never parsed a
        // mapping file (design D3).
        var act = () => InsertStackAsync("dup-state", ("status:ready-for-proposal", "/ds:other"));

        await Should.ThrowAsync<Exception>(act);
    }

    [Fact]
    public async Task Schema_Should_RemoveStatesWithTheirStack()
    {
        await InsertStackAsync("cascade-me", ("status:ready-for-proposal", "/ds:propose"));

        await using var db = postgres.NewContext();
        await db.Database.ExecuteSqlRawAsync(
            "delete from configstore.stacks where \"Id\" = {0}",
            "cascade-me"
        );

        // An orphan state entry must be unrepresentable rather than merely unlikely (design D7).
        var orphans = await db
            .Database.SqlQuery<int>(
                $"""select count(*)::int as "Value" from configstore.mapping_states where "StackId" = 'cascade-me'"""
            )
            .SingleAsync();

        orphans.ShouldBe(0);
    }

    [Fact]
    public async Task Schema_Should_StoreEnumsAsReadableStrings()
    {
        await InsertStackAsync("enum-check", ("status:ready-for-proposal", "/ds:propose"));

        await using var db = postgres.NewContext();
        var stored = await db
            .Database.SqlQuery<string>(
                $"""select "Vendor" as "Value" from configstore.stacks where "Id" = 'enum-check'"""
            )
            .SingleAsync();

        // Names, not ordinals: the column stays readable in a manual query, and inserting a vendor into
        // the middle of the enum cannot silently re-point existing rows.
        stored.ShouldBe("GitHub");
    }
}
