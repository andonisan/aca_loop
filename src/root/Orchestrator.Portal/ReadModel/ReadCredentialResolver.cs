using Orchestrator.Portal.Registry;

namespace Orchestrator.Portal.ReadModel;

/// <summary>Resolves a stack's read-scoped vendor credential from its Key Vault secret name.</summary>
interface IReadCredentialResolver
{
    Task<string> ResolveAsync(RegisteredStack stack, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the secret through configuration, which is where <c>AddOptionalAzureKeyVault</c> has already
/// placed Key Vault's contents. Secret names use <c>--</c> where a configuration key uses <c>:</c>, so the
/// registry's stored name is translated rather than requiring operators to write the configuration form.
/// <para>
/// Fails loudly on a missing secret. The alternative — returning empty and letting the vendor answer 401 —
/// would surface a configuration mistake as an authentication failure, which sends whoever is debugging it
/// to the wrong place entirely.
/// </para>
/// </summary>
sealed class ReadCredentialResolver(IConfiguration configuration) : IReadCredentialResolver
{
    public Task<string> ResolveAsync(
        RegisteredStack stack,
        CancellationToken cancellationToken = default
    )
    {
        var secretName = stack.Vendor.ReadCredentialSecretName;
        var value = configuration[secretName.Replace("--", ":")];

        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException(
                $"read-scoped credential '{secretName}' for stack '{stack.Id}' is not available; "
                    + "check the secret exists in Key Vault and the portal's identity can read it"
            );
        }

        return Task.FromResult(value);
    }
}
