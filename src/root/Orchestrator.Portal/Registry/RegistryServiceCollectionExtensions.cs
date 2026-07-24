namespace Orchestrator.Portal.Registry;

public static class RegistryServiceCollectionExtensions
{
    /// <summary>
    /// Loads and validates the stack registry eagerly, before the host is built, so an invalid
    /// registry throws and the process never reaches <c>app.Run()</c> — the same "fail fast, serve
    /// no traffic" contract the receiver applies to its mapping file (portal-read-model spec).
    /// </summary>
    public static IServiceCollection AddStackRegistry(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var options = new RegistryOptions();
        configuration.GetSection(RegistryOptions.SectionName).Bind(options);

        var registry = StackRegistryLoader.LoadFromFile(options.FilePath);

        services.AddSingleton(registry);

        return services;
    }
}
