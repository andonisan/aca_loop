using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Orchestrator.ArchitectureAnalyzers;

/// <summary>
/// A module's persistence entities must stay internal to it.
/// <para>
/// This rule had nothing to guard until the ConfigStore module introduced the product's first schema.
/// It matters most here, of all places: the config store decides which board states spend agent money,
/// so a second module binding directly to its rows would make every migration a cross-module breaking
/// change on an authorization surface.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Mod003PublicDomainEntityAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [DiagnosticDescriptors.Mod003PublicDomainEntity];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.DeclaredAccessibility != Accessibility.Public || !type.IsPersistenceEntity())
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                DiagnosticDescriptors.Mod003PublicDomainEntity,
                type.Locations[0],
                type.Name
            )
        );
    }
}
