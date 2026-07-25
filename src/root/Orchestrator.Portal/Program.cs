using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Orchestrator.Constants;
using Orchestrator.Modules.ConfigStore;
using Orchestrator.Portal.Components;
using Orchestrator.Portal.LocalDev;
using Orchestrator.Portal.ReadModel;
using Orchestrator.Portal.Registry;
using Orchestrator.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Eager, before Build(): an invalid registry must stop the process, not surface per request.
builder.Services.AddStackRegistry(builder.Configuration);

// Explicitly, not via RegisterAppModules: the portal references the vendor modules for their read-only
// clients but must not register their adapters, whose options it does not configure.
builder.Services.AddConfigStore(builder.Configuration);

builder.Services.AddReadModel(builder.Configuration);

// Entra ID sign-in against a single internal app registration. Nothing but the health probes is
// reachable unauthenticated, enforced by a fallback policy rather than per-page attributes so a new
// page is protected by default instead of by remembering to annotate it (portal-auth spec).
//
// The one exception is a local-development stand-in principal, which exists so the UI can be worked
// on without an app registration. It cannot engage outside Development regardless of configuration.
if (!LocalDevAuthentication.TryAdd(builder))
{
    builder
        .Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
}

builder
    .Services.AddAuthorizationBuilder()
    .AddPolicy(
        PortalPolicies.Read,
        policy => policy.RequireRole(PortalRoles.Reader, PortalRoles.Operator)
    )
    .AddPolicy(PortalPolicies.Operate, policy => policy.RequireRole(PortalRoles.Operator))
    // The fallback requires only a signed-in user, deliberately not a role: a tenant user with no
    // portal role must reach the app far enough to be *told* which role to ask for (portal-auth
    // spec) rather than getting a bare 403. The role itself is enforced one layer in — MainLayout
    // gates every page on the Read policy, and each verb endpoint will require Operate explicitly.
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

// Razor Pages carries Microsoft.Identity.Web's sign-in/sign-out endpoints only; no application page
// is a Razor Page.
builder.Services.AddRazorPages().AddMicrosoftIdentityUI();

var app = builder.Build();

// Health/liveness first and anonymous (MapDefaultEndpoints allows anonymous), so platform probes
// never depend on Entra being reachable.
app.MapDefaultEndpoints();

app.UseAuthentication();
app.UseAuthorization();

// Required by the Razor Components endpoints, and must sit after authn/authz.
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapRazorPages();

app.Run();
