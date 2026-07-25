using Microsoft.CodeAnalysis;

namespace Orchestrator.ArchitectureAnalyzers;

public static class DiagnosticDescriptors
{
    const string ModuleBoundaryCategory = "ModuleBoundary";

    public static readonly DiagnosticDescriptor Mod001PublicCqsRequest = new(
        id: "MOD001",
        title: "Domain events, commands, and queries must be internal within a module",
        messageFormat: "'{0}' is a CQS command/query and must be internal to its module",
        category: ModuleBoundaryCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Commands and queries carry a module's internal application logic and must not "
            + "be part of its public contract. Cross-module callers use the module's .Contracts project."
    );

    public static readonly DiagnosticDescriptor Mod002CrossModuleReference = new(
        id: "MOD002",
        title: "Cross-module injection of another module's internal types is forbidden",
        messageFormat: "'{0}' references '{1}' from module '{2}' directly — reference its .Contracts project instead",
        category: ModuleBoundaryCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A module may depend only on another module's .Contracts project, never its "
            + "implementation assembly."
    );

    public static readonly DiagnosticDescriptor Mod003PublicDomainEntity = new(
        id: "MOD003",
        title: "Persistence entities must be internal within a module",
        messageFormat: "'{0}' is a persistence entity and must be internal to its module",
        category: ModuleBoundaryCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A module's entities are its private storage shape, not its contract. Making one "
            + "public lets another module bind to a schema it does not own, which is what turns a "
            + "migration into a cross-module breaking change. Consumers get a DTO from .Contracts."
    );

    public static readonly DiagnosticDescriptor Mod005EntityLeak = new(
        id: "MOD005",
        title: "Persistence entities must not appear in a module's public signatures",
        messageFormat: "'{0}' exposes persistence entity '{1}' — return a contract type instead",
        category: ModuleBoundaryCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Keeping entities internal is not enough on its own: a public member that returns "
            + "or accepts one still leaks the storage shape to callers. MOD003 covers the type, this "
            + "covers the signatures that would carry it out."
    );

    public static readonly DiagnosticDescriptor Mod004ControllerInModule = new(
        id: "MOD004",
        title: "ASP.NET MVC controllers must not exist inside module projects",
        messageFormat: "'{0}' is an MVC controller — modules expose HTTP behaviour via IUseCase minimal APIs",
        category: ModuleBoundaryCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Modules expose HTTP behaviour as IUseCase minimal-API endpoints, never MVC controllers."
    );

    public static readonly DiagnosticDescriptor Cqs001PublicOrUnsealedHandler = new(
        id: "CQS001",
        title: "CQS handlers must be internal sealed",
        messageFormat: "'{0}' implements a CQS handler interface and must be internal and sealed",
        category: ModuleBoundaryCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "CQS handler types must stay internal to their module and sealed — one handler "
            + "per request, never subclassed or exposed across the module boundary."
    );
}
