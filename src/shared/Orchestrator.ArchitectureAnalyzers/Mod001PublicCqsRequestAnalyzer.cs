using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Orchestrator.ArchitectureAnalyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Mod001PublicCqsRequestAnalyzer : DiagnosticAnalyzer
{
    const string CqsNamespace = "Orchestrator.BuildingBlocks.CQS";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [DiagnosticDescriptors.Mod001PublicCqsRequest];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.DeclaredAccessibility != Accessibility.Public)
        {
            return;
        }

        if (
            !type.ContainingAssembly.Name.StartsWith(
                SymbolExtensions.ModuleAssemblyPrefix,
                System.StringComparison.Ordinal
            ) || type.ContainingAssembly.IsContractsAssembly()
        )
        {
            return;
        }

        var isCqsRequest =
            type.ImplementsOpenGeneric(CqsNamespace, "ICommand", 1)
            || type.ImplementsOpenGeneric(CqsNamespace, "IQuery", 1);

        if (isCqsRequest)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.Mod001PublicCqsRequest,
                    type.Locations[0],
                    type.Name
                )
            );
        }
    }
}
