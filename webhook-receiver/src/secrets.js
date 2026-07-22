import { DefaultAzureCredential } from "@azure/identity";
import { SecretClient } from "@azure/keyvault-secrets";

const client = new SecretClient(process.env.KEY_VAULT_URI, new DefaultAzureCredential());

let cache = null;

// Secrets are fetched once at process start and kept in memory only --
// never written to disk, never logged.
export async function loadSecrets() {
  const webhookSecret = await client.getSecret("github-webhook-secret");

  cache = {
    githubWebhookSecret: webhookSecret.value,
  };

  return cache;
}

export function getSecrets() {
  if (!cache) {
    throw new Error("secrets not loaded yet");
  }
  return cache;
}
