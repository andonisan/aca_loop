namespace Orchestrator.Constants;

/// <summary>Names used to declare/resolve Aspire resources and their connection strings.</summary>
public static class Components
{
    public const string Storage = "storage";
    public const string JobsQueue = "ai-jobs";
    public const string KeyVault = "keyvault";
    public const string Receiver = "receiver";
    public const string Agent = "agent";
    public const string Portal = "portal";

    /// <summary>The Postgres database holding the configuration store. One physical database; modules
    /// isolate by schema, following ds-connect's convention.</summary>
    public const string Database = "database";
}
