using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Orchestrator.ArchitectureAnalyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Cqs001PublicOrUnsealedHandlerAnalyzer : DiagnosticAnalyzer
{
    const string CqsNamespace = "Orchestrator.BuildingBlocks.CQS";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [DiagnosticDescriptors.Cqs001PublicOrUnsealedHandler];

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

        var isHandler =
            type.ImplementsOpenGeneric(CqsNamespace, "IAppCommandHandler", 2)
            || type.ImplementsOpenGeneric(CqsNamespace, "IAppQueryHandler", 2);

        if (isHandler && (type.DeclaredAccessibility != Accessibility.Internal || !type.IsSealed))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.Cqs001PublicOrUnsealedHandler,
                    type.Locations[0],
                    type.Name
                )
            );
        }
    }
}
