using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orchestrator.BuildingBlocks.Modules;

namespace Orchestrator.Modules.ConfigStore;

/// <summary>
/// Registers the configuration store. Composed by the Portal, which owns this schema, and by the Receiver,
/// which only reads it.
/// <para>
/// <b>This module does not migrate.</b> Migration belongs to the schema's owner and there must be exactly
/// one of it — the Portal, of which there is one. Receivers load configuration on every start; hundreds of
/// them calling <c>MigrateAsync</c> is the race that rule exists to prevent, so a reader that meets an
/// unexpected schema version fails rather than fixing it (design D8).
/// </para>
/// </summary>
public sealed class ConfigStoreModule : WorkerModuleBase
{
    /// <summary>Delegates to the shared registration so a module-discovering host and an explicitly
    /// composing one wire this identically.</summary>
    public override void Add(IServiceCollection services, IConfiguration configuration) =>
        services.AddConfigStore(configuration);

}
