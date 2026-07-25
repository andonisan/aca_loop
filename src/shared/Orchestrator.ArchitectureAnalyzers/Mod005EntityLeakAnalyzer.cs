using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Orchestrator.ArchitectureAnalyzers;

/// <summary>
/// The other half of MOD003: an entity kept internal still escapes if a public member hands one out.
/// <para>
/// Generic arguments are unwrapped, because <c>Task&lt;Stack&gt;</c> and <c>IReadOnlyList&lt;Stack&gt;</c>
/// leak exactly as much as a bare <c>Stack</c> and are how it would actually happen.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Mod005EntityLeakAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Unwrapping deeper than a nested generic buys nothing: past
    /// <c>Task&lt;IReadOnlyList&lt;T&gt;&gt;</c> the shapes stop occurring in real signatures.</summary>
    const int MaxGenericDepth = 2;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [DiagnosticDescriptors.Mod005EntityLeak];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
    }

    static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;

        if (
            !IsEffectivelyPublic(method)
            || method.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor)
            || !method.ContainingAssembly.IsModuleAssembly()
        )
        {
            return;
        }

        var leaked =
            FindEntity(method.ReturnType)
            ?? method.Parameters.Select(p => FindEntity(p.Type)).FirstOrDefault(t => t is not null);

        if (leaked is not null)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.Mod005EntityLeak,
                    method.Locations[0],
                    method.Name,
                    leaked.Name
                )
            );
        }
    }

    static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;

        if (!IsEffectivelyPublic(property) || !property.ContainingAssembly.IsModuleAssembly())
        {
            return;
        }

        if (FindEntity(property.Type) is not { } leaked)
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                DiagnosticDescriptors.Mod005EntityLeak,
                property.Locations[0],
                property.Name,
                leaked.Name
            )
        );
    }

    /// <summary>A member is part of the public surface only if it, and every type enclosing it, is
    /// public — a public property on an internal class goes nowhere.</summary>
    static bool IsEffectivelyPublic(ISymbol member)
    {
        if (member.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        for (
            var containing = member.ContainingType;
            containing is not null;
            containing = containing.ContainingType
        )
        {
            if (containing.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The offending entity itself, not the type that carries it. Reporting the outer type would name
    /// <c>List</c> for a <c>List&lt;Stack&gt;</c> and call it a persistence entity, which is both wrong
    /// and points the reader at the wrong thing to change.
    /// </summary>
    static ITypeSymbol? FindEntity(ITypeSymbol type, int depth = 0)
    {
        if (type.IsPersistenceEntity())
        {
            return type;
        }

        if (
            depth >= MaxGenericDepth
            || type is not INamedTypeSymbol { IsGenericType: true } generic
        )
        {
            return null;
        }

        return generic
            .TypeArguments.Select(argument => FindEntity(argument, depth + 1))
            .FirstOrDefault(found => found is not null);
    }
}
