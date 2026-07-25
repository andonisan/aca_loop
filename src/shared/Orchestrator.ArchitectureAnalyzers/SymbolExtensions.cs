using System;
using Microsoft.CodeAnalysis;

namespace Orchestrator.ArchitectureAnalyzers;

static class SymbolExtensions
{
    public const string ModuleAssemblyPrefix = "Orchestrator.Modules.";
    const string ContractsAssemblySuffix = ".Contracts";

    public static bool ImplementsOpenGeneric(
        this ITypeSymbol type,
        string containingNamespace,
        string interfaceName,
        int arity
    )
    {
        foreach (var candidate in type.AllInterfaces)
        {
            if (
                candidate.Arity == arity
                && candidate.Name == interfaceName
                && candidate.ContainingNamespace?.ToDisplayString() == containingNamespace
            )
            {
                return true;
            }
        }

        return false;
    }

    public static bool InheritsFrom(
        this ITypeSymbol? type,
        string containingNamespace,
        string typeName
    )
    {
        for (var current = type?.BaseType; current is not null; current = current.BaseType)
        {
            if (
                current.Name == typeName
                && current.ContainingNamespace?.ToDisplayString() == containingNamespace
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns the module name (e.g. "GitHub") for an assembly named "Orchestrator.Modules.GitHub[.Contracts]", or null.</summary>
    public static string? GetModuleName(this IAssemblySymbol assembly)
    {
        var name = assembly.Name;
        if (!name.StartsWith(ModuleAssemblyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var moduleName = name.Substring(ModuleAssemblyPrefix.Length);
        return moduleName.EndsWith(ContractsAssemblySuffix, StringComparison.Ordinal)
            ? moduleName.Substring(0, moduleName.Length - ContractsAssemblySuffix.Length)
            : moduleName;
    }

    public static bool IsContractsAssembly(this IAssemblySymbol assembly) =>
        assembly.Name.EndsWith(ContractsAssemblySuffix, StringComparison.Ordinal);

    public static bool IsModuleAssembly(this IAssemblySymbol assembly) =>
        assembly.Name.StartsWith(ModuleAssemblyPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Whether a type is one of a module's persistence entities.
    /// <para>
    /// ds-connect answers this by checking for an <c>Aggregate</c>/<c>BaseEntity</c> base class, which is
    /// why MOD003 and MOD005 were skipped when this repo adopted the analyzer set — those base types do
    /// not exist here, and adding them purely to give an analyzer something to match would import DDD
    /// scaffolding a stateless product has no use for.
    /// </para>
    /// <para>
    /// The convention that does exist is the folder layout: entities live in
    /// <c>Features/&lt;Feature&gt;/Domain/</c>, so their namespace ends in <c>.Domain</c>. Matching on that
    /// keeps the marker in one place — move a type out of Domain and it stops being an entity, which is
    /// the same thing the layout already says.
    /// </para>
    /// </summary>
    public static bool IsPersistenceEntity(this ITypeSymbol? type) =>
        type?.ContainingAssembly is { } assembly
        && assembly.IsModuleAssembly()
        && !assembly.IsContractsAssembly()
        && type.ContainingNamespace?.ToDisplayString() is { } ns
        && (ns.EndsWith(DomainNamespaceSuffix, StringComparison.Ordinal) || ns == "Domain");

    const string DomainNamespaceSuffix = ".Domain";
}
