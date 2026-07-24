using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Orchestrator.ArchitectureAnalyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Mod004ControllerInModuleAnalyzer : DiagnosticAnalyzer
{
    const string MvcNamespace = "Microsoft.AspNetCore.Mvc";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [DiagnosticDescriptors.Mod004ControllerInModule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (
            !type.ContainingAssembly.Name.StartsWith(
                SymbolExtensions.ModuleAssemblyPrefix,
                System.StringComparison.Ordinal
            ) || type.ContainingAssembly.IsContractsAssembly()
        )
        {
            return;
        }

        if (
            type.InheritsFrom(MvcNamespace, "Controller")
            || type.InheritsFrom(MvcNamespace, "ControllerBase")
        )
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.Mod004ControllerInModule,
                    type.Locations[0],
                    type.Name
                )
            );
        }
    }
}
