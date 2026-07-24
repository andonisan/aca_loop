namespace Orchestrator.Portal.Components;

/// <summary>
/// Named outlets <c>MainLayout</c> exposes in the topbar so a page can place its title, breadcrumbs,
/// and actions in the chrome while still owning them.
/// </summary>
/// <remarks>
/// The indirection exists for focus management: <c>Routes.razor</c> focuses <c>h1</c> after every
/// navigation, which only works while the heading lives in the page's own render tree. Sections let
/// the heading render in the topbar without moving it into the layout.
/// </remarks>
static class PortalSections
{
    /// <summary>Breadcrumbs and the page title (<c>.breadcrumbs</c> + <c>.page-title-row</c>).</summary>
    public const string PageHeader = "page-header";

    /// <summary>Page-level actions, rendered right-aligned in the topbar.</summary>
    public const string PageActions = "page-actions";
}
