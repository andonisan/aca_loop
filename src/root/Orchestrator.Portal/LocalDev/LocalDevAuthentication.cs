using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Orchestrator.Constants;

namespace Orchestrator.Portal.LocalDev;

public sealed class LocalDevAuthOptions
{
    public const string SectionName = "Portal:LocalDevAuth";

    /// <summary>Opt-in flag. Honoured **only** in the Development environment — see
    /// <see cref="LocalDevAuthentication.TryAdd"/>. Off unless something sets it.</summary>
    public bool Enabled { get; init; }

    public string UserName { get; init; } = "local-dev@example.invalid";

    /// <summary>Roles the stand-in principal carries; defaults to full access so every view renders.</summary>
    public string[] Roles { get; init; } = [PortalRoles.Reader, PortalRoles.Operator];
}

/// <summary>
/// A stand-in sign-in for local development, so the UI can be worked on without an Entra app
/// registration. This is authentication-bypass code, so it is fenced twice: it is registered only
/// when the host environment is Development **and** the opt-in flag is set, and the environment check
/// comes first so setting the flag in a deployed environment does nothing. The container image runs
/// as Production, which is what makes that fence load-bearing rather than decorative.
/// </summary>
public static class LocalDevAuthentication
{
    public const string SchemeName = "LocalDev";

    /// <summary>
    /// Registers the stand-in scheme and returns true if it took effect, so the caller can wire real
    /// OpenID Connect otherwise. Never returns true outside Development.
    /// </summary>
    public static bool TryAdd(WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment())
        {
            return false;
        }

        var options = new LocalDevAuthOptions();
        builder.Configuration.GetSection(LocalDevAuthOptions.SectionName).Bind(options);

        if (!options.Enabled)
        {
            return false;
        }

        builder.Services.AddSingleton(options);
        builder
            .Services.AddAuthentication(SchemeName)
            .AddScheme<AuthenticationSchemeOptions, LocalDevAuthHandler>(SchemeName, _ => { });

        return true;
    }
}

sealed class LocalDevAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    LocalDevAuthOptions devOptions
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims =
        [
            new(ClaimTypes.Name, devOptions.UserName),
            .. devOptions.Roles.Select(role => new Claim(ClaimTypes.Role, role)),
        ];

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, LocalDevAuthentication.SchemeName)
        );

        return Task.FromResult(
            AuthenticateResult.Success(
                new AuthenticationTicket(principal, LocalDevAuthentication.SchemeName)
            )
        );
    }
}
