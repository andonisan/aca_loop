namespace Orchestrator.Constants;

/// <summary>
/// The Portal's Entra ID app roles. Assignment happens in Entra (via group membership), never in the
/// application — the portal only reads the roles off the signed-in principal.
/// </summary>
public static class PortalRoles
{
    /// <summary>Read every view. Grants no ability to spend agent budget or stop a client's run.</summary>
    public const string Reader = "Portal.Reader";

    /// <summary>Reads plus the operational verbs (manual dispatch, cancel).</summary>
    public const string Operator = "Portal.Operator";
}

/// <summary>
/// Authorization policies built from <see cref="PortalRoles"/>. <see cref="Operate"/> is enforced on
/// the verb handlers themselves, not merely by hiding UI, so a crafted request from a Reader is
/// rejected server-side (portal-auth spec).
/// </summary>
public static class PortalPolicies
{
    public const string Read = "portal:read";
    public const string Operate = "portal:operate";
}
