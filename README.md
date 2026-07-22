# AI Loop on Azure Container Apps

Label-driven cloud agents for ds-connect's issue lifecycle. When an issue
reaches one of the two **machine-gate** labels, a scale-to-zero Container
Apps Job spins up an agent (Claude Code or opencode) that clones the repo
and runs the repo's own command headlessly:

| Gate label | Command the agent runs |
|---|---|
| `status:ready-for-proposal` | `/ds:propose` — OpenSpec change on a branch as a draft PR |
| `status:ready-for-implementation` | `/ds:implement` — implement on the same PR |

Humans stay exactly where the process already puts them (grill, spec
review, code review, `/ds:sync`); the loop is closed by moving labels —
requesting changes and re-applying a gate label re-dispatches the agent,
which then sees the reviewer's PR comments.

```
GitHub (issues: labeled, gate labels only)
        │ webhook (HMAC-signed)
        ▼
Container App "webhook-receiver"  (always-on, min_replicas=1)
  - verifies signature, filters action=="labeled" + gate label match
  - drops events from the automation's own bot (no self-triggering)
  - enqueues {repo, issue_number, gate_label, agent_runtime}
        │
        ▼
Storage Queue "ai-jobs" ──(KEDA azure-queue rule)──▶ Container Apps Job "agent"
                                                       (Event trigger, 0..N)
                                                       each execution:
                                                       1. dequeues exactly one message
                                                          (empty? exit 0 -- KEDA overscale
                                                          is expected)
                                                       2. re-validates the gate label on the
                                                          live issue (moved? exit 0 -- the
                                                          single-label invariant is the lock)
                                                       3. mints a per-repo installation token
                                                          (GitHub App, never a static token)
                                                       4. clones the repo, runs /ds:propose
                                                          or /ds:implement via claude/opencode
                                                       5. on failure: issue -> status:blocked
                                                          + comment with the log tail
                                                       6. deletes the message
```

This stack is standalone and mirrors the patterns of the ds3d
`github-runner` stack (managed identity end to end, no connection strings,
secrets only in Key Vault, JIT-minted GitHub credentials per execution).
It deliberately shares nothing with it: independent blast radius, deploys,
and lifecycle. See `openspec/changes/ai-loop-aca-job/` for the proposal,
design decisions, and specs.

## Layout

- [`terraform/`](./terraform/) — the full Azure stack (resource group, storage + queue, Key Vault, ACA environment, receiver app, agent job, identities).
- [`webhook-receiver/`](./webhook-receiver/) — the `issues`-event receiver.
- [`agent/`](./agent/) — the agent image: entrypoint + Claude Code + opencode + `gh`.

## Manual prerequisites (cannot be Terraformed)

1. Org Settings → Developer settings → **GitHub Apps** → New GitHub App.
   This is a **dedicated App** — do not reuse a CI runner App; keeping the
   permission sets separate keeps the blast radius separate.
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

## Rollout / verification

1. Apply into a scratch resource group first.
2. Complete the manual GitHub App steps above, including the secrets.
3. Test the receiver alone: use GitHub's webhook "Redeliver" on a past
   `issues` labeled delivery with a gate label, then check
   `az storage message peek --account-name <name> --queue-name ai-jobs
   --auth-mode login`.
4. Live run: label a scratch issue `status:ready-for-proposal`, watch
   `az containerapp job execution list --name <agent_job_name> -g <rg>`,
   and confirm a draft PR opens and the label advances.
5. Test the edge cases deliberately:
   - re-label the issue away **before** the execution starts → it must
     exit 0 with no LLM calls in the logs (stale gate);
   - kill an execution mid-run → the issue must end at `status:blocked`
     with an explanatory comment;
   - redeliver the same webhook twice → the second execution must exit 0
     at gate re-validation;
   - fill the WIP limit (four `status:in-progress` issues) and dispatch a
     fifth `/ds:implement` → the gate label must be kept, with a comment.
6. Review loop: request changes on the draft PR, re-label to
   `status:ready-for-proposal`, and confirm the agent revises using the
   review comments.

## Known trade-offs

- **Always-on receiver.** `min_replicas=1` plus Storage/Key Vault/Log
  Analytics is a small fixed cost; everything else bills per execution.
- **Agent runs are long.** `replica_timeout_seconds` (default 3600) is a
  hard ceiling per run; the entrypoint's watchdog fires 120s earlier so
  the failure contract runs before the platform kills the replica. LLM
  spend per run is bounded by `agent_max_turns`.
- **No automatic retry of agent work.** A failed run is not
  idempotent-retryable like a CI job — the issue goes to `status:blocked`
  and a human re-labels to retry. Only entrypoint-level crashes retry via
  the queue's visibility timeout, capped by the poison-message guard.
- **Prompt injection is a real surface.** Issue bodies drive an agent
  holding a repo-scoped write token. Controls: label-triage as the
  authorization gate, per-repo short-lived tokens, branch protection,
  turn caps, and an allowed-tools policy (`AGENT_ALLOWED_TOOLS`).
- **Copilot CLI is out of scope.** Its auth is tied to a user
  subscription; use GitHub's hosted Copilot coding agent natively instead.
