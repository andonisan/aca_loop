# Orchestrator Portal — centralized web control plane over per-client stacks

## Why

The orchestrator ships as one stack per client (multi-tenancy decision resolved in `agentic-backlog-orchestrator` design: per-client blast radius). That leaves operators with no single place to answer "what are the loops doing?" across clients: gated items, queue depth, running AI pods, and run outcomes live scattered across vendor boards, Azure portals, and Log Analytics workspaces. The `agentic-backlog-orchestrator` change deliberately excluded any UI ("configuration is a versioned file"); this change adds the operational surface *above* the stacks without reopening that decision.

Direction chosen during exploration (2026-07-24): a **web portal, not a TUI** — a centralized service reaches cross-client operations in one place and needs no client-side install; a TUI remains possible later as operator sugar. **Internal-only** access for now (Plain Concepts operators via Entra ID); client-facing access is explicitly out of scope.

## What Changes

- Add a new host `Orchestrator.Portal` (Blazor, following the existing ds-connect-derived solution conventions) — one central deployment, not per-client.
- The portal is a **read-model stitcher over existing APIs — no new datastore**:
  - Items currently in mapped gate states → vendor APIs via the existing adapter modules (reused as a library).
  - Queue depth and running/recent AI-pod executions → Azure Storage + ACA Jobs APIs via managed identity.
  - Run history, success rate, durations → Log Analytics (Azure Monitor cross-workspace queries over the structured OTel events the stacks already emit; add/normalize one dispatch-outcome event if needed).
  - Per-client mapping config → rendered read-only from the deployed config artifacts.
- Add **small operational verbs** (each a single queue/ACA API call): retry a failed dispatch, cancel a running pod, manually trigger a mapped action. Every verb is audited (structured log with actor identity).
- **Authentication**: Entra ID, internal tenant only. No client/tenant-facing authorization model in this change.
- **Guardrail — no config write path**: the mapping stays a versioned, deploy-time file (authorization surface per D2 of the orchestrator design). If config editing is ever added, it is GitOps-only (portal produces a PR; a human merges; the stack redeploys) — out of scope here.

## Capabilities

### New Capabilities
- `portal-read-model`: the cross-stack aggregation — vendor gate views, queue/pod status, Log Analytics-backed run history — and how each source is queried (identities, scopes, workspace discovery per client stack).
- `portal-operations`: the operational verbs (retry, cancel, manual dispatch), their preconditions against live state, and the audit contract.
- `portal-auth`: Entra ID sign-in, internal-only access policy, and the managed identities/permissions the portal itself needs against client stacks (reader on ACA/Storage/Log Analytics; vendor credentials read-only reuse).

### Modified Capabilities
<!-- none — the per-client stacks are untouched; the portal only reads their existing surfaces. If a normalized dispatch-outcome log event is needed, it lands as an additive tweak inside the orchestrator's observability behavior. -->

## Impact

- **Code**: new `src/root/Orchestrator.Portal` host; vendor adapter modules gain read-only consumers (no behavior change); possibly one normalized `[LoggerMessage]` dispatch-outcome event in `Execution`/`Dispatch`.
- **Infra (`terraform/`)**: one new central Container App (the portal), one Entra ID app registration, cross-stack RBAC grants (Log Analytics Reader, Storage/ACA readers, plus queue-send/job-cancel for the verbs).
- **Security surface**: the portal concentrates read access to all client stacks — internal-only Entra auth is the compensating control; operational verbs are the only write paths and are audited. No vendor write credentials are exposed to the portal beyond what the verbs strictly need (none in v1 — verbs act on Azure resources, not vendor items).
- **Dependencies**: assumes per-client stacks emit the observability events from the orchestrator's ported observability spec; assumes per-client-stack tenancy (resolved).

## Out of Scope

- Client-facing access / per-tenant authorization.
- Any config editing (GitOps or otherwise).
- TUI or CLI dashboard (a validation CLI belongs to the orchestrator change; an operator TUI may come later).
- Live attach/steer of running opencode sessions (potential later differentiator; spike via opencode session sharing first).
