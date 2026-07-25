# Agentic Backlog Orchestrator

Multi-vendor, mapping-driven cloud agents for issue/work-item lifecycles.
When a tracked item (a GitHub issue, a GitLab issue, an Azure DevOps work
item, a Jira issue) reaches a state declared in a deployment's mapping file, a
scale-to-zero Container Apps Job — an **AI pod** — spins up an agent
(Claude Code or opencode) that clones the target repo and runs that state's
mapped command headlessly:

```yaml
# src/root/Orchestrator.Receiver/mapping.yaml (this repo's shipped sample:
# v1's ds-connect config, reproduced exactly)
targets:
  - vendor: GitHub
    repo: "your-org/ds-connect"
    states:
      "status:ready-for-proposal":
        action: /ds:propose
        runtime: Claude
        onFailureState: "status:blocked"
      "status:ready-for-implementation":
        action: /ds:implement
        runtime: Claude
        onFailureState: "status:blocked"
```

Which state triggers which command, on which runtime, and where a failed
run gets moved — all of that is **configuration**, not code (see
`state-action-config` in `openspec/changes/agentic-backlog-orchestrator/`).
Humans stay exactly where the process already puts them (grill, spec
review, code review, sync); the loop is closed by moving the state —
requesting changes and re-applying a gate state re-dispatches the agent,
which then sees the reviewer's comments.

**Orient here:**

- [`docs/context.md`](./docs/context.md) — why this exists, the actors, the domain terms.
- [`ARCHITECTURE.md`](./ARCHITECTURE.md) — the components, the pipeline, the load-bearing invariants, where the code lives.
- [`DESIGN.md`](./DESIGN.md) — design rationale, risks/trade-offs, open questions; decisions in [`docs/adr/`](./docs/adr/).
- [`ONBOARDING.md`](./ONBOARDING.md) — zero to first contribution, including end-to-end verification.
- [`AGENTS.md`](./AGENTS.md) — entry point for AI agents working in this repo.

The rest of this file is the vendor reference and deployment quick-start.

## Vendors

A vendor can be a **tracker** (it holds the work item and its state), a
**code host** (it holds the git repo the agent clones), or both. The three
original vendors are both. Jira is a tracker only.

| Vendor | Role | Status | Adapter |
|---|---|---|---|
| GitHub | Tracker + code host | Always on, the only vendor v1 shipped | `src/modules/GitHub/` |
| GitLab | Tracker + code host | Optional (`enable_gitlab`) | `src/modules/GitLab/` |
| Azure DevOps | Tracker + code host | Optional (`enable_azure_devops`), PAT auth | `src/modules/AzureDevOps/` |
| Jira | **Tracker only** | Optional (`enable_jira`) | `src/modules/Jira/` |

Because Jira hosts no git, every Jira target must also declare where its
code lives. `codeHost` defaults to the target's own `vendor` and `codeRepo`
to its `repo`, so single-vendor mappings need no change; a Jira target has
to set both:

```yaml
  - vendor: Jira                    # tracker: matched on project key
    project: "DSC"
    codeHost: GitHub                # where the git repo lives
    codeRepo: "your-org/ds-connect"
    states:
      "Ready for Proposal":
        action: /ds:propose
        runtime: Claude
        onFailureState: "Blocked"
```

Jira also differs in what carries the state: the workflow **status**, not a
label. That gives a native single-state invariant, but status cannot be
written directly — it moves only through a transition that is valid from the
item's *current* status, so a configured `onFailureState` can legitimately be
unreachable. When that happens the failure comment is still posted and the
run is still recorded; only the state is left as it was.

GitLab, Azure DevOps, and Jira compile, are unit-tested, and are wired into
Terraform — but none has been run against a live instance yet (that needs a
real org/PAT/token this repo's dev environment doesn't have). Treat them as
implemented-but-unpiloted until someone runs the rollout checklist below
against a real project of each.

For Jira specifically, note that the pilot has to exercise the **failure**
path, not just the happy one: Cloud and Data Center diverge on REST version,
comment format, and auth together (`/rest/api/3` + Atlassian Document Format
are Cloud-only), and the comment call is the first place that divergence
executes.

## Architecture

```
Vendor (GitHub | GitLab | Azure DevOps)
        │ webhook (HMAC / secret token / Basic auth, per vendor)
        ▼
Container App "receiver"  (always-on, min_replicas=1)
  - verifies the webhook through the matching vendor adapter
  - normalizes it to a CanonicalEvent {vendor, project, repo, item_id, native_state, actor}
  - drops events from the automation's own bot identity (no self-triggering)
  - resolves it against mapping.yaml -> {action, runtime, on_failure_state}
    (no match = not a gate state = no-op, not an error)
  - enqueues the CanonicalMessage
        │
        ▼
Storage Queue "ai-jobs" ──(KEDA azure-queue rule)──▶ Container Apps Job "agent" ("AI pod")
                                                       (Event trigger, 0..N)
                                                       each execution:
                                                       1. dequeues exactly one message
                                                          (empty? exit 0 -- KEDA overscale
                                                          is expected)
                                                       2. re-validates the gate state on the
                                                          live item (moved? exit 0 -- the
                                                          single-state invariant is the lock)
                                                       3. mints per-run credentials via the
                                                          matching vendor adapter (never a
                                                          static token)
                                                       4. clones the repo, runs the mapped
                                                          action via claude/opencode
                                                       5. on failure: item -> its mapped
                                                          on_failure_state + comment with the
                                                          log tail (success write-back is the
                                                          target repo's own command's job, not
                                                          this infra's)
                                                       6. deletes the message
```

This stack is standalone and mirrors the patterns of the ds3d
`github-runner` stack (managed identity end to end, no connection strings,
secrets only in Key Vault, JIT-minted credentials per execution). It
deliberately shares nothing with it: independent blast radius, deploys, and
lifecycle. See `openspec/changes/agentic-backlog-orchestrator/` for the
proposal, design decisions, and specs (and `openspec/changes/ai-loop-aca-job/`
for the original GitHub-only v1 this was generalized from — not yet archived).

## Layout

The backend is a .NET Aspire solution under `src/`, organized like
ds-connect (see `openspec/changes/agentic-backlog-orchestrator/design.md`
D7 for the full rationale):

- [`src/root/Orchestrator.Receiver/`](./src/root/Orchestrator.Receiver/) — the always-on webhook host; also where `mapping.yaml` lives.
- [`src/root/Orchestrator.Agent/`](./src/root/Orchestrator.Agent/) — the Container Apps Job entrypoint (one execution = one dequeue attempt).
- [`src/root/Orchestrator.Aspire/`](./src/root/Orchestrator.Aspire/) — local dev orchestration (Receiver + Agent + Azurite for the queue). `enable_azure_devops`/GitLab need no emulator; only the queue does.
- [`src/modules/Backlog/`](./src/modules/Backlog/) — the canonical contracts (`CanonicalEvent`, `CanonicalMessage`, `IBacklogAdapter`, `ICodeHostAdapter`) shared by every vendor module.
- [`src/modules/GitHub/`](./src/modules/GitHub/), [`GitLab/`](./src/modules/GitLab/), [`AzureDevOps/`](./src/modules/AzureDevOps/) — one vendor adapter each, tracker **and** code host.
- [`src/modules/Jira/`](./src/modules/Jira/) — tracker only; implements `IBacklogAdapter` and deliberately not `ICodeHostAdapter`.
- [`src/modules/Dispatch/`](./src/modules/Dispatch/) — the webhook endpoint and queue writer (composed by Receiver only), over [`Dispatch.Contracts/`](./src/modules/Dispatch/Orchestrator.Modules.Dispatch.Contracts/) — the mapping model, loader/validator, and resolver, extracted so the Portal reuses the same validator rather than a copy of it.
- [`src/modules/Execution/`](./src/modules/Execution/) — gate re-validation, runtime execution, failure write-back (composed by Agent only).
- [`src/tests/`](./src/tests/) — `Orchestrator.ArchTests` (module-boundary rules) + one `.UnitTests` project per module.
- [`terraform/`](./terraform/) — the full Azure stack (resource group, storage + queue, Key Vault, ACA environment, receiver app, agent job, identities, per-vendor secrets).

## Manual prerequisites (cannot be Terraformed)

### GitHub (always required)

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
6. Install the App on the target repos only; note the numeric **App ID**
   and **Installation ID** plus the App's **bot login** (`<app-slug>[bot]`)
   — these go into `terraform.tfvars`.
7. Run `terraform apply` (see below).
8. Push the secrets into Key Vault out-of-band — **never** through a `.tf`
   variable or state:
   ```bash
   az keyvault secret set --vault-name <vault> \
     --name Vendors--GitHub--PrivateKeyPem --file /path/to/private-key.pem
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
    to change the tracked state is the authorization gate for spending
    agent time.**

### GitLab (optional, `enable_gitlab = true`)

1. Create a project or group access token, scoped to `api` +
   `write_repository`, on the target project(s).
2. Push it to Key Vault: `az keyvault secret set --vault-name <vault> --name Vendors--GitLab--AccessToken --value <token>`.
3. Take the `azure_devops_webhook_password`-style output —
   `terraform output -raw gitlab_webhook_secret` — and configure a project
   webhook: URL `https://<fqdn>/webhooks/gitlab`, Secret token = that
   output, trigger on **Issues events**.

### Jira (optional, `enable_jira = true`)

1. Create a dedicated Jira account for the automation (its account is what
   the self-trigger filter keys on) and note its **accountId** — visible in
   the URL of its profile page, or via
   `/rest/api/3/myself`. On Data Center there are no account ids; use the
   username instead. Set `jira_bot_account_id` to it.
2. Create the token for that account. **Cloud**: an API token
   (id.atlassian.com → Security → API tokens), and set `jira_account_email`
   to the account's email — Cloud auth is Basic with email + token.
   **Data Center**: a personal access token, which is sent as a bearer token
   and needs no email.
3. Push it to Key Vault: `az keyvault secret set --vault-name <vault> --name Vendors--Jira--ApiToken --value <token>`.
4. Grant that account, on the target project: Browse Projects, Add Comments,
   and **Transition Issues**. Transition permission is the one that matters —
   it is what lets the agent move an item to its failure state.
5. Register the webhook as an **admin webhook** — Jira Settings → System →
   WebHooks, or `POST /rest/webhooks/1.0/webhook`. Do **not** use
   `/rest/api/3/webhook`: that path is restricted to Connect/OAuth 2.0 apps
   and its webhooks **expire 30 days** from creation or last refresh, which
   would need a refresh job to keep a per-client stack alive unattended.
   Admin webhooks have no stated expiry.
6. Configure it: URL `https://<fqdn>/webhooks/jira`, event **Issue updated**
   (`jira:issue_updated`), scoped with a JQL filter to the target project
   (e.g. `project = DSC`), and **Secret** =
   `terraform output -raw jira_webhook_secret`.
7. The secret is not optional here even though Atlassian's UI treats it as
   such: the adapter rejects any request without a valid `X-Hub-Signature`,
   so leaving it blank breaks dispatch outright rather than merely weakening
   it.
8. Set `jira_deployment` to `Cloud` or `DataCenter`. This one value selects
   the REST version, the comment body format, and the auth scheme together —
   getting it wrong surfaces on the comment call, which is on the failure
   path, so verify it by forcing a failed run rather than a successful one.
9. In the mapping, give every Jira target a `codeHost` + `codeRepo`, and
   complete that code host's own prerequisites above (its token and clone
   credentials are what the agent actually pushes with). A Jira target's
   `codeHost` must be a vendor whose module is composed in this deployment —
   the receiver refuses to start otherwise.
10. On the target Jira project: restrict who holds **Transition Issues** for
    the gate statuses — **the ability to change the tracked state is the
    authorization gate for spending agent time**, and on Jira that gate is
    workflow permissions and conditions rather than a label permission.
    Jira's model is generally the more restrictive of the four, which is
    favourable, but it is per-project and must be checked per client.

### Azure DevOps (optional, `enable_azure_devops = true`)

1. Create a Personal Access Token scoped to Work Items (Read & write) and
   Code (Read), on the target organization/project.
2. Push it to Key Vault: `az keyvault secret set --vault-name <vault> --name Vendors--AzureDevOps--PersonalAccessToken --value <pat>`.
3. Project Settings → Service Hooks → new subscription → **Web Hooks**,
   trigger **Work item updated**, URL `https://<fqdn>/webhooks/azure-devops`,
   Basic authentication: username = `var.azure_devops_webhook_username`
   (default `aca-hook`), password = `terraform output -raw azure_devops_webhook_password`.
4. PAT auth is what's implemented today (design.md's open questions record
   why, and that an Entra service principal is a same-shape future swap).

## Usage

```bash
cd terraform
cp terraform.tfvars.example terraform.tfvars
# fill in: subscription_id, github_app_id, github_app_installation_id,
# github_bot_login, container_registry_*, *_image
# (optionally: enable_gitlab / enable_azure_devops / enable_jira + their variables)

terraform init
terraform plan
terraform apply
```

Build and push the two images before the first `apply` (the Container App
and Job pull them immediately). Both build with **`src/` as the context**
— they reference sibling shared/modules projects:

```bash
docker build -f src/root/Orchestrator.Receiver/Dockerfile -t <registry>/ai-loop-receiver:latest src/
docker push <registry>/ai-loop-receiver:latest

docker build -f src/root/Orchestrator.Agent/Dockerfile -t <registry>/ai-loop-agent:latest src/
docker push <registry>/ai-loop-agent:latest
```

### Local dev loop

`mapping.yaml` ships baked into the Receiver image (it's a `Content` item
copied to the publish output); to point at your own targets locally,
edit `src/root/Orchestrator.Receiver/mapping.yaml` before building, or
override `Mapping:FilePath` in configuration. Aspire (`src/root/Orchestrator.Aspire`)
composes Receiver + Agent + an Azurite emulator for the queue — no Azure
subscription needed for the dispatch pipeline itself; Key Vault is skipped
locally (`Orchestrator.ServiceDefaults.AddOptionalAzureKeyVault` is a no-op
when `KEY_VAULT_URI` is unset), so vendor secrets come from `appsettings.Development.json`
or user-secrets instead.

## Rollout / verification

1. Apply into a scratch resource group first.
2. Complete the manual GitHub App steps above, including the secrets.
3. Test the receiver alone: use GitHub's webhook "Redeliver" on a past
   `issues` labeled delivery with a mapped state, then check
   `az storage message peek --account-name <name> --queue-name ai-jobs
   --auth-mode login`.
4. Live run: move a scratch item to `status:ready-for-proposal`, watch
   `az containerapp job execution list --name <agent_job_name> -g <rg>`,
   and confirm a draft PR opens and the state advances.
5. Test the edge cases deliberately:
   - move the item away **before** the execution starts → it must exit 0
     with no LLM calls in the logs (stale gate);
   - kill an execution mid-run → the item must end at its mapped
     `onFailureState` with an explanatory comment;
   - redeliver the same webhook twice → the second execution must exit 0
     at gate re-validation;
   - fill the WIP limit (four `status:in-progress` items) and dispatch a
     fifth `/ds:implement` → the gate state must be kept, with a comment
     (the target repo's own command enforces this — the orchestrator has
     no second, independent WIP guard by design; see design.md's open
     questions).
6. Review loop: request changes on the draft PR, move back to
   `status:ready-for-proposal`, and confirm the agent revises using the
   review comments.
7. If GitLab/Azure DevOps are enabled, repeat 3-6 against a scratch project
   of each — this is the still-open verification gap (tasks.md 4.4/5.5).

## Known trade-offs

- **Always-on receiver.** `min_replicas=1` plus Storage/Key Vault/Log
  Analytics is a small fixed cost; everything else bills per execution.
- **Agent runs are long.** `replica_timeout_seconds` (default 3600) is a
  hard ceiling per run; the entrypoint's watchdog fires 120s earlier so
  the failure contract runs before the platform kills the replica. LLM
  spend per run is bounded by `agent_max_turns`.
- **No automatic retry of agent work.** A failed run is not
  idempotent-retryable like a CI job — the item goes to its mapped
  `onFailureState` and a human moves it back to retry. Only
  entrypoint-level crashes retry via the queue's visibility timeout,
  capped by the poison-message guard.
- **Prompt injection is a real surface.** Item bodies/comments drive an
  agent holding a repo-scoped write credential. Controls: the "who can
  change tracked state" permission is the authorization gate, per-run
  short-lived-where-possible credentials (GitHub App tokens are JIT-minted;
  GitLab/Azure DevOps tokens are longer-lived PATs — a known asymmetry, see
  design.md D6), branch protection, turn caps, and an allowed-tools policy
  (`Execution:AllowedTools`).
- **No orchestrator-side WIP guard.** Resolved by design, not an oversight
  — see design.md's open questions.
- **GitLab, Azure DevOps, and Jira are unpiloted.** Implemented and
  unit-tested, never run against a live instance. Don't treat them as
  production-ready until someone closes that gap. For Jira, the Cloud vs
  Data Center divergence only executes on the failure path, so a
  happy-path pilot does not cover it.
- **Copilot CLI is out of scope.** Its auth is tied to a user
  subscription; use GitHub's hosted Copilot coding agent natively instead.

Then verify the deployment end to end — receiver, live run, and the deliberate edge cases — following [`ONBOARDING.md`](./ONBOARDING.md#3-verify-it-end-to-end).
