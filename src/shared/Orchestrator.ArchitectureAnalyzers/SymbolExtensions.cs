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
}
