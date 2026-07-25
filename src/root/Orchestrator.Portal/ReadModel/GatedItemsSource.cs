using Microsoft.Extensions.Options;
using Orchestrator.Modules.Backlog.Contracts;
using Orchestrator.Modules.Dispatch.Contracts;
using Orchestrator.Portal.Registry;

namespace Orchestrator.Portal.ReadModel;

/// <summary>
/// The work items currently sitting in one of a stack's gate states — what the loop is about to act on, or
/// is waiting on a human for.
/// <para>
/// The gate states come from that stack's own mapping, read through the same loader its receiver uses, so
/// the portal cannot show a different set of gates than the one that actually dispatches. Items in unmapped
/// states never appear: a state the mapping does not declare is not a gate, and listing it would suggest
/// the loop is about to do something it will not.
/// </para>
/// </summary>
sealed class GatedItemsSource(BacklogReaderFactory readers, IOptions<ReadModelOptions> options)
    : IStackSource<IReadOnlyList<BacklogItem>>
{
    public string Name => "gated-items";

    public TimeSpan Ttl => options.Value.GatedItemsTtl;

    public async Task<IReadOnlyList<BacklogItem>> ReadAsync(
        RegisteredStack stack,
        CancellationToken cancellationToken = default
    )
    {
        var mapping = MappingLoader.LoadFromFile(stack.MappingFilePath);

        // A stack's mapping can declare several targets; the gate view is about this stack's own vendor
        // coordinates, so only matching targets contribute states.
        var states = mapping
            .Targets.Where(t =>
                t.Matches(stack.Vendor.Vendor, stack.Vendor.Project, stack.Vendor.Repo)
            )
            .SelectMany(t => t.States.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (states.Length == 0)
        {
            return [];
        }

        return await readers
            .For(stack)
            .ListItemsInStatesAsync(
                stack.Vendor.Project,
                stack.Vendor.Repo,
                states,
                cancellationToken
            );
    }
}
