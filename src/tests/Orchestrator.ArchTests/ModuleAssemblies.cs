using System.Reflection;

namespace Orchestrator.ArchTests;

/// <summary>
/// Discovers module assemblies the same way <c>ModuleExtensions.DiscoverModules</c> does at
/// runtime, so adding a module needs no edit here either.
/// </summary>
static class ModuleAssemblies
{
    public const string ModuleAssemblyPrefix = "Orchestrator.Modules.";
    const string ContractsAssemblySuffix = ".Contracts.dll";

    public static IReadOnlyList<Assembly> Implementations { get; } =
    [
        .. Directory
            .GetFiles(AppContext.BaseDirectory, $"{ModuleAssemblyPrefix}*.dll")
            .Where(path => !path.EndsWith(ContractsAssemblySuffix, StringComparison.Ordinal))
            .Select(Assembly.LoadFrom),
    ];
}
