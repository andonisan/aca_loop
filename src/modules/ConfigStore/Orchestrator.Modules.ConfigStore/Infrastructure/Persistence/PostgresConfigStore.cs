using Microsoft.EntityFrameworkCore;
using Orchestrator.Modules.ConfigStore.Contracts;
using Orchestrator.Modules.ConfigStore.Features.Stacks.Domain;

namespace Orchestrator.Modules.ConfigStore.Infrastructure.Persistence;

/// <summary>
/// The read path. Projects entities to Contracts DTOs at the boundary, so no caller ever holds an entity —
/// the rule MOD005 enforces once ported, and the reason this module needs no repository interface to be
/// safe.
/// </summary>
sealed class PostgresConfigStore(ConfigStoreDbContext dbContext) : IConfigStore
{
    public async Task<IReadOnlyList<StoredStack>> ListStacksAsync(
        CancellationToken cancellationToken = default
    )
    {
        var stacks = await Query().OrderBy(s => s.Id).ToListAsync(cancellationToken);
        return [.. stacks.Select(ToDto)];
    }

    public async Task<StoredStack?> GetStackAsync(
        string id,
        CancellationToken cancellationToken = default
    )
    {
        var stack = await Query().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        return stack is null ? null : ToDto(stack);
    }

    /// <summary>No tracking: everything through this type is a read, and tracked entities would be both
    /// wasted work and a way for a caller to mutate state that has no business changing here.</summary>
    IQueryable<Stack> Query() => dbContext.Stacks.AsNoTracking().Include(s => s.States);

    /// <summary>
    /// Runs in memory, after materialization, deliberately.
    /// <para>
    /// EF cannot translate a call to an ordinary method inside a projection — <c>Select(s => ToDto(s))</c>
    /// compiles and then throws at query time. The alternatives are inlining the whole shape as an
    /// expression, which buries the mapping in the query and gets worse with the nested states, or
    /// materializing first. Materializing is right here: this is hundreds of rows, read rarely and cached
    /// behind a TTL, so the round trip costs nothing worth optimizing and the mapping stays readable.
    /// </para>
    /// </summary>
    static StoredStack ToDto(Stack s) =>
        new(
            s.Id,
            s.DisplayName,
            new StoredAzureCoordinates(
                s.SubscriptionId,
                s.ResourceGroup,
                new Uri(s.QueueServiceUri),
                s.QueueName,
                s.JobName,
                s.LogAnalyticsWorkspaceId
            ),
            new StoredVendorCoordinates(
                s.Vendor,
                s.VendorProject,
                s.VendorRepo,
                s.ReadCredentialSecretName,
                new Uri(s.VendorBaseUrl),
                s.VendorDeployment
            ),
            [
                .. s.States.Select(m => new StoredState(
                    m.NativeState,
                    m.Action,
                    m.Runtime,
                    m.OnSuccessState,
                    m.OnFailureState,
                    m.CodeHost,
                    m.CodeRepo
                )),
            ]
        );
}
