# AI Loop on Azure Container Apps

Label-driven cloud agents for ds-connect's issue lifecycle. When an issue reaches one of the two **machine-gate** labels (`status:ready-for-proposal` → `/ds:propose`, `status:ready-for-implementation` → `/ds:implement`), a scale-to-zero Container Apps Job spins up an agent (Claude Code or opencode) that clones the repo and runs the repo's own command headlessly. Humans stay exactly where the process already puts them; the loop is closed by moving labels.

**Orient here:**

- [`docs/context.md`](./docs/context.md) — why this exists, the actors, the domain terms.
- [`ARCHITECTURE.md`](./ARCHITECTURE.md) — the components, the pipeline, the load-bearing invariants, where the code lives.
- [`DESIGN.md`](./DESIGN.md) — design rationale, risks/trade-offs, open questions; decisions in [`docs/adr/`](./docs/adr/).
- [`ONBOARDING.md`](./ONBOARDING.md) — zero to first contribution, including end-to-end verification.
- [`AGENTS.md`](./AGENTS.md) — entry point for AI agents working in this repo.

The rest of this file is the deployment quick-start.

## Manual prerequisites (cannot be Terraformed)

1. Org Settings → Developer settings → **GitHub Apps** → New GitHub App.
   This is a **dedicated App** — do not reuse a CI runner App; keeping the
   permission sets separate keeps the blast radius separate
   ([ADR-0003](./docs/adr/0003-dedicated-github-app-separate-from-ci-runner-app.md)).
2. **Repository permissions**: Issues **Read & write**, Contents **Read &
   write**, Pull requests **Read & write**.
3. **Subscribe to events → Issues.**
4. Webhook: Active. Leave the URL as a placeholder for now (it depends on
   `webhook_receiver_fqdn`, which only exists after the first `apply`) —
   you'll set it in step 8.
5. Generate a private key (.pem) for the App.
6. Install the App on the target repos only (ds-connect first); note the
   numeric **App ID** and **Installation ID** plus the App's **bot login**
   (`<app-slug>[bot]`) — these go into `terraform.tfvars`.
7. Run `terraform apply` (see below).
8. Push the secrets into Key Vault out-of-band — **never** through a `.tf`
   variable or state:
   ```bash
   az keyvault secret set --vault-name <vault> \
     --name github-app-private-key --file /path/to/private-key.pem
   az keyvault secret set --vault-name <vault> \
     --name anthropic-api-key --value <key>
   # optional, extra env for opencode providers as a JSON object:
   az keyvault secret set --vault-name <vault> \
     --name opencode-provider-keys --value '{"OPENROUTER_API_KEY":"..."}'
   ```
9. Take the `webhook_receiver_fqdn` output and the `github_webhook_secret`
   output (`terraform output -raw github_webhook_secret`), and set the
   GitHub App's webhook URL to `https://<fqdn>/webhooks/github` with that
   secret.
10. In the target repo: ensure branch protection on the default branch
    (the agent must only ever push branches and open PRs) and that only
    people meant to dispatch agents have triage permission — **the ability
    to apply labels is the authorization gate for spending agent time.**

## Usage

```bash
cd terraform
cp terraform.tfvars.example terraform.tfvars
# fill in: subscription_id, github_app_id, github_app_installation_id,
# agent_bot_login, container_registry_*, *_image

terraform init
terraform plan
terraform apply
```

Build and push the two images before the first `apply` (the Container App
and Job pull them immediately):

```bash
docker build -t <registry>/ai-loop-webhook:latest ./webhook-receiver
docker push <registry>/ai-loop-webhook:latest

docker build -t <registry>/ai-loop-agent:latest ./agent
docker push <registry>/ai-loop-agent:latest
```

Then verify the deployment end to end — receiver, live run, and the deliberate edge cases — following [`ONBOARDING.md`](./ONBOARDING.md#3-verify-it-end-to-end).
