namespace Orchestrator.Constants;

/// <summary>
/// Shared column lengths, so the same kind of value is the same width everywhere and a schema review is
/// about which bucket a column belongs in rather than about a number someone picked. Mirrors ds-connect's
/// constants of the same name.
/// </summary>
public static class DbFieldLengths
{
    /// <summary>Identifiers and enum-ish values stored as strings — stack ids, vendor names, runtimes.</summary>
    public const int Code = 100;

    /// <summary>Names and coordinates: display names, repo paths, project keys, resource names.</summary>
    public const int Name = 400;

    /// <summary>Free text with no natural bound in practice: actions, state names, secret names.</summary>
    public const int Text = 1000;

    /// <summary>URLs. Matches the conventional practical maximum.</summary>
    public const int Url = 2048;
}
