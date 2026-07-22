import { DefaultAzureCredential } from "@azure/identity";
import { SecretClient } from "@azure/keyvault-secrets";

const client = new SecretClient(process.env.KEY_VAULT_URI, new DefaultAzureCredential());

// Secrets are fetched once per execution and kept in memory only --
// never written to disk, never logged.
export async function loadSecrets() {
  const [appId, privateKey, anthropicKey, opencodeKeys] = await Promise.all([
    client.getSecret("github-app-id"),
    client.getSecret("github-app-private-key"),
    getOptionalSecret("anthropic-api-key"),
    getOptionalSecret("opencode-provider-keys"),
  ]);

  return {
    githubAppId: appId.value,
    githubAppPrivateKey: privateKey.value,
    anthropicApiKey: anthropicKey,
    // JSON object of extra env vars for opencode providers, e.g.
    // {"OPENROUTER_API_KEY": "..."} -- optional, pushed out-of-band.
    opencodeProviderEnv: opencodeKeys ? JSON.parse(opencodeKeys) : {},
  };
}

async function getOptionalSecret(name) {
  try {
    const secret = await client.getSecret(name);
    return secret.value;
  } catch (err) {
    if (err.statusCode === 404) return null;
    throw err;
  }
}
