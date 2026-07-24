using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Orchestrator.ArchitectureAnalyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Mod002CrossModuleReferenceAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [DiagnosticDescriptors.Mod002CrossModuleReference];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    static void AnalyzeField(SymbolAnalysisContext context)
    {
        var field = (IFieldSymbol)context.Symbol;
        Check(context, field.ContainingAssembly, field.Type, field, field.Name);
    }

    static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;
        Check(context, property.ContainingAssembly, property.Type, property, property.Name);
    }

    static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;

        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor))
        {
            return;
        }

        foreach (var parameter in method.Parameters)
        {
            Check(context, method.ContainingAssembly, parameter.Type, method, method.Name);
        }
    }

    static void Check(
        SymbolAnalysisContext context,
        IAssemblySymbol containingAssembly,
        ITypeSymbol referencedType,
        ISymbol reportOn,
        string memberName
    )
    {
        var ownModule = containingAssembly.GetModuleName();
        if (ownModule is null)
        {
            return;
        }

        var referencedAssembly = referencedType.ContainingAssembly;
        if (referencedAssembly is null)
        {
            return;
        }

        var referencedModule = referencedAssembly.GetModuleName();
        if (
            referencedModule is null
            || referencedModule == ownModule
            || referencedAssembly.IsContractsAssembly()
        )
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                DiagnosticDescriptors.Mod002CrossModuleReference,
                reportOn.Locations[0],
                memberName,
                referencedType.Name,
                referencedModule
            )
        );
    }
}
