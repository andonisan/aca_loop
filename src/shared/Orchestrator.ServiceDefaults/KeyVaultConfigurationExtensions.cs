using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Orchestrator.ServiceDefaults;

public static class KeyVaultConfigurationExtensions
{
    /// <summary>
    /// Adds Azure Key Vault as a configuration source when <c>KEY_VAULT_URI</c> is set (deployed in
    /// Azure) — a no-op locally (Aspire/Azurite dev loop has no Key Vault). Secret names use
    /// <c>--</c> as the config-hierarchy separator, since Key Vault names can't contain <c>:</c>:
    /// e.g. <c>Vendors--GitHub--WebhookSecret</c> becomes configuration key
    /// <c>Vendors:GitHub:WebhookSecret</c>. Read via the host's managed identity — never a static
    /// credential, never a secret in Terraform state (see design.md D6).
    /// </summary>
    public static TBuilder AddOptionalAzureKeyVault<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var keyVaultUri = builder.Configuration["KEY_VAULT_URI"];
        if (!string.IsNullOrWhiteSpace(keyVaultUri))
        {
            builder.Configuration.AddAzureKeyVault(
                new Uri(keyVaultUri),
                new DefaultAzureCredential()
            );
        }

        return builder;
    }
}
