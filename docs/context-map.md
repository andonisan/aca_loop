# Context map

How this stack relates to its neighbours. Unlike an application's DDD context map, this repo is a single bounded context — the map that matters is **the systems around it and the contract on each edge**. For the internal component view, see [`ARCHITECTURE.md`](../ARCHITECTURE.md).

## Map

```
                 GitHub (org)
   ┌───────────────────────────────────────────┐
   │  GitHub App "ai-loop"     target repos    │
   │  (webhook + permissions)  (ds-connect, …) │
   └───────┬───────────────────────▲───────────┘
           │ ①  issues webhook     │ ③  REST + git, JIT token
           ▼                       │
   ┌───────────────┐   ②   ┌───────┴────────┐        ④
   │ webhook       │──────▶│ agent job      │──────▶ LLM providers
   │ receiver (ACA)│ queue │ (ACA Job, KEDA)│        (Anthropic, …)
   └───────────────┘       └────────────────┘
           └──────── Azure: RG · Storage · Key Vault · ACA env ────────┘

   (pattern sibling, shares nothing: ds3d github-runner stack)
```

## Edges

### ① GitHub → webhook receiver — *inbound webhooks*

- **What crosses**: `issues` event deliveries, HMAC-SHA256 signed with the App's webhook secret, to `POST /webhooks/github`.
- **Contract**: the receiver enqueues only `action == "labeled"` where the label is a machine gate and the sender is not this automation's own bot ([ADR-0002](./adr/0002-machine-gate-labels-as-the-only-trigger.md)). Everything else is acknowledged and dropped.

### ② Receiver → agent job — *the queue*

- **What crosses**: one base64-encoded JSON work item `{repo, issue_number, gate_label, agent_runtime, enqueued_at}` per dispatch, on Storage Queue `ai-jobs`.
- **Contract**: KEDA scales one job execution per queued message; each execution dequeues at most one item, hidden for the full replica timeout so a crashed execution's message reappears (capped by the poison guard at 3 dequeues).

### ③ Agent job → GitHub — *outbound writes*

- **What crosses**: an installation token minted per execution and scoped to the single target repo (expires ≤1h, never stored — [ADR-0008](./adr/0008-managed-identity-jit-tokens-secrets-only-in-key-vault.md)); then `git clone`/push over HTTPS, issue/label/comment writes via REST, and whatever `gh` calls the repo's own `/ds:*` command makes.
- **Contract with target repos** (what this stack *assumes* about ds-connect and any future repo):
  - The `status:*` **single-label invariant** and the two **gate labels** `status:ready-for-proposal` / `status:ready-for-implementation`.
  - The repo defines `/ds:propose` and `/ds:implement` as agent-runnable commands (tool-neutral via `AGENTS.md` + `.claude/commands` / `.opencode/commands`), and those commands own the label flips, branching, and PR handling.
  - **Branch protection** on the default branch — the agent must only ever push branches and open PRs.
  - **Triage permission is the authorization gate**: only people meant to spend agent time can apply labels.
  - `/ds:implement` enforces its own WIP limit and refuses with output matching `WIP_REFUSAL_PATTERN` (default `"WIP limit"`), which this stack treats as keep-the-gate-label, not failure.

### ④ Agent job → LLM providers — *outbound inference*

- **What crosses**: the agent CLI's API traffic. Claude Code uses `ANTHROPIC_API_KEY` (Key Vault secret `anthropic-api-key`); opencode uses provider keys from the optional `opencode-provider-keys` secret (a JSON object of env vars). Spend per run is bounded by `AGENT_MAX_TURNS` and the replica timeout.

### Azure — *the substrate*

Everything runs in one resource group: the Storage account (queue), Key Vault (secrets), Container Apps environment (+ Log Analytics), the receiver app, and the agent job. All intra-Azure auth is managed identity with least-privilege RBAC; there are no connection strings ([ADR-0008](./adr/0008-managed-identity-jit-tokens-secrets-only-in-key-vault.md)).

### ds3d `github-runner` stack — *pattern sibling*

The proven webhook→queue→KEDA-job machine this stack mirrors: same idioms (managed identity end to end, JIT-minted GitHub credentials, poison-message guards, "overscale is normal, exit clean"). **Zero shared resources**: separate GitHub App, separate resource group, separate lifecycle and blast radius ([ADR-0003](./adr/0003-dedicated-github-app-separate-from-ci-runner-app.md), [ADR-0004](./adr/0004-standalone-stack-with-sibling-receiver.md)).
