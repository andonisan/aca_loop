# Architecture

A bird's-eye map of how this stack is built — the components, the load-bearing invariants, and where to find things. Read this first to orient, then follow the pointers. For *why* it exists, see [`docs/context.md`](./docs/context.md); for the design rationale behind each choice, see [`DESIGN.md`](./DESIGN.md) and [`docs/adr/`](./docs/adr/); the behavioural source of truth is [`openspec/`](./openspec/) — this file does not restate it.

## The big picture

Three deployed pieces on Azure Container Apps, connected by one Storage Queue:

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

## Components

| Component | What it is | Key numbers (Terraform defaults) |
| --- | --- | --- |
| **webhook-receiver** | Tiny Express app (`GET /healthz`, `POST /webhooks/github`); verifies HMAC-SHA256, filters, enqueues. Stateless. | always-on `min_replicas=1`, `max_replicas=2`, 0.25 vCPU / 0.5 Gi |
| **ai-jobs queue** | Azure Storage Queue; one base64 JSON work item per dispatch. | KEDA `queueLength=1`, polled every 30 s |
| **agent job** | Event-triggered Container Apps Job; one execution per work item runs `agent/src/entrypoint.js`. | 0→`max_concurrent_agents` (4) executions, 1 vCPU / 2 Gi, `replica_timeout_seconds` 3600, `replica_retry_limit=0` |
| **agent image** | node:22 + git + `gh` + Claude Code + opencode; runtime picked per work item ([ADR-0006](./docs/adr/0006-single-agent-image-runtime-selected-per-message.md)). | `AGENT_MAX_TURNS` 80, `AGENT_ALLOWED_TOOLS` env-tunable |
| **Key Vault** | The only home for secrets: `github-webhook-secret`, `github-app-id`, `github-app-private-key`, `anthropic-api-key`, optional `opencode-provider-keys`. | RBAC-authorized; app key + provider keys pushed out-of-band |
| **Identities** | Two user-assigned managed identities (receiver, agent job) with least-privilege RBAC — the receiver can read exactly one secret and only send to the queue. | see `terraform/identities.tf` |
| **Observability** | Log Analytics workspace wired to the ACA environment; container stdout/stderr is the log stream. | 30-day retention |

## Load-bearing invariants

Break one of these and the system's correctness story falls apart — change them only with a new ADR:

1. **Only machine-gate labels dispatch, and the loop cannot trigger itself.** The receiver enqueues only `action=="labeled"` for `GATE_LABELS`; the agent's own label writes are never gate labels, and its bot's events are dropped. [ADR-0002](./docs/adr/0002-machine-gate-labels-as-the-only-trigger.md)
2. **The single-label invariant is the lock.** No distributed locks; each execution re-validates the gate label on the live issue and exits clean if it moved. [ADR-0005](./docs/adr/0005-execution-time-gate-re-validation-as-the-concurrency-lock.md)
3. **Failure never leaves the board lying.** Trap + watchdog (fires 120 s before the replica timeout) flip the issue to `status:blocked` with evidence; agent work is never auto-retried (poison guard caps crash-retries at 3 dequeues). WIP-limit refusals keep the gate label. [ADR-0007](./docs/adr/0007-failure-contract-status-blocked-plus-comment-no-auto-retry.md)
4. **Managed identity end to end; no connection strings; JIT GitHub tokens.** Per-run installation token, single repo, ≤1 h, never stored; secrets only in Key Vault. [ADR-0008](./docs/adr/0008-managed-identity-jit-tokens-secrets-only-in-key-vault.md)
5. **Issue content is untrusted input.** Authorization is GitHub triage permission (labeling); blast radius is bounded by token scope, branch protection, turn caps, and the allowed-tools policy. [ADR-0009](./docs/adr/0009-guardrails-for-untrusted-issue-content.md)
6. **No agent logic in this infra.** The agent runs the *target repo's own* commands (`/ds:propose`, `/ds:implement`); this stack owns only the dispatch/execution contract around them.

## Where the code lives

| Path | What |
| --- | --- |
| [`terraform/`](./terraform/) | The full Azure stack: resource group, storage + queue, Key Vault + RBAC, ACA environment, receiver app, agent job, the two identities. |
| [`webhook-receiver/`](./webhook-receiver/) | The `issues`-event receiver (`src/index.js`, plus queue/secrets clients). |
| [`agent/`](./agent/) | The agent image: `src/entrypoint.js` (dequeue → re-validate → clone → run → failure contract), GitHub App token minting, Dockerfile. |
| [`docs/`](./docs/) | Orientation docs: [`context.md`](./docs/context.md), [`context-map.md`](./docs/context-map.md), and [`adr/`](./docs/adr/). |
| [`openspec/`](./openspec/) | Changes and specs — the behavioural source of truth (`changes/ai-loop-aca-job/` is the founding change). |
| [`docs/products/`](./docs/products/) | Product-side proposals (multi-vendor orchestrator exploration); not part of the deployed stack. |

## Configuration surface

Everything tunable lives in [`terraform/variables.tf`](./terraform/variables.tf) (gate labels, default runtime, concurrency, timeouts, turn caps, images) plus two image-level env vars overridable per revision without rebuild: `AGENT_ALLOWED_TOOLS` and `WIP_REFUSAL_PATTERN`. Deployment prerequisites and rollout steps live in [`README.md`](./README.md); verification in [`ONBOARDING.md`](./ONBOARDING.md).
