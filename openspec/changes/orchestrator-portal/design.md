# Design — Orchestrator Portal

## Context

The orchestrator ships as **one stack per client** (decision resolved 2026-07-24 in the `agentic-backlog-orchestrator` design): webhook receiver + Storage Queue + KEDA-scaled ACA Job per client, stateless by design — the vendor board is the state machine, the queue is the only buffer, structured OTel logs land in Log Analytics per the ported observability spec. There is deliberately no UI anywhere and no config write path other than redeploying versioned files (orchestrator D2).

Operators (Plain Concepts, internal) therefore have no single place to see what the loops are doing across clients: gated items live on vendor boards, queue/pod state in each Azure resource group, run outcomes in per-stack Log Analytics workspaces. This change adds a **central, internal-only web control plane** above the stacks. Direction fixed during exploration: web portal (not TUI), read-model stitcher (no new datastore), Entra ID internal-only, small audited operational verbs, and a hard "no config editing" guardrail.

The .NET solution already provides the raw material: vendor adapter modules (`IVendorAdapter` with read methods), `Dispatch`'s mapping loader/resolver, shared kernel + ServiceDefaults + MOD analyzers, and ds-connect-derived conventions for adding a host.

## Goals / Non-Goals

**Goals:**
- One central portal deployment showing, per client stack: items currently in mapped gate states, queue depth, running/recent AI-pod executions, and run history (success rate, durations, failure tails).
- Operational verbs — retry/manual-dispatch an action for an item, cancel a running pod execution — each a single Azure API call, audited with the operator's identity.
- Entra ID sign-in, internal tenant only, with a read/operate role split.
- Zero portal-owned persistence; every view derives from live sources or Log Analytics.

**Non-Goals:**
- No config editing of any kind (mapping or stack registry) through the portal — GitOps redeploy remains the only write path for configuration.
- No client/tenant-facing access or per-tenant authorization model.
- No alerting/notification engine (Log Analytics alerts cover this natively if wanted).
- No TUI/CLI dashboard; no live attach/steer of running agent sessions (both parked as possible later changes).
- No changes to the per-client stacks' runtime behavior beyond one additive, normalized dispatch-outcome log event.

## Decisions

### D1 — A fourth host in the same solution: `Orchestrator.Portal` (Blazor Server)
The portal is `src/root/Orchestrator.Portal`, a Blazor Server app (interactive server rendering) composed from the same shared kernel, ServiceDefaults (OTel, health endpoints), and module system as Receiver/Agent. It reuses the vendor adapter modules read-only and `Dispatch`'s mapping model for resolution. Views refresh by polling with per-source cache TTLs (queue depth ~10s, executions ~15s, vendor items ~60s, history ~5min) — no SignalR fan-out beyond what Blazor Server already provides, no client-side API to design.
*Alternatives considered*: separate repo + SPA over a new REST API — rejected: duplicates the adapter/mapping logic behind a bespoke API for one internal audience; Blazor Server keeps it one codebase, one auth stack, and the MOD analyzers apply unchanged. TUI — rejected in exploration (centralized service, no client-side install, cross-client ops).

### D2 — Stack registry is a versioned file, same philosophy as the mapping
A `stacks.yaml` deployed with the portal lists each client stack: id/display name, Azure coordinates (subscription, resource group, queue endpoint + name, ACA job name, Log Analytics workspace id), vendor coordinates, and a reference to that stack's mapping file (bundled at deploy time from the same repo/GitOps source that deploys the stack). Validated eagerly at startup; invalid registry → fail fast, refuse to serve (mirrors the receiver's mapping behavior).
*Alternative considered*: auto-discovery via Azure resource tags — attractive later, but implicit membership in a surface that concentrates read access across all clients is the wrong default; an explicit versioned list is itself an authorization artifact.

### D3 — Run history comes from Log Analytics, not a portal database
Each stack already exports structured OTel logs. This change adds one normalized `[LoggerMessage]` **dispatch-outcome event** (in `Execution`, emitted at run end: vendor, project/repo, item id, action, runtime, outcome ∈ {succeeded, failed, stale-gate, poison}, duration, log-tail pointer) plus its dispatch-side sibling (enqueue event). The portal runs cross-workspace KQL via Azure Monitor Query against the workspaces in `stacks.yaml`. History depth = workspace retention; that is accepted and documented.
*Alternative considered*: portal-owned store (Postgres/Table Storage) fed by events — rejected: introduces the product's first persistence layer, an ingestion path, and a second source of truth, to answer questions KQL already answers.

### D4 — Verbs are queue/ACA calls that reuse Dispatch's resolution; gate semantics preserved
- **Manual dispatch / retry**: portal resolves `{item, native_state}` through the stack's bundled mapping (same `MappingLoader`/resolver as the receiver), reads the item's live state via the vendor adapter, and only enqueues the canonical message if the item currently carries the mapped state — the portal cannot bypass the gate invariant; the agent's own re-validation remains the second lock. Retry of a failed run is the same verb pointed at the item after a human returns it to the gate state (the portal offers this as one flow but the state transition is done by the human on the board or via the vendor write-back — the portal itself does not write vendor state in v1).
- **Cancel**: stop the specific ACA job execution via the ACA management API. The queue message for that execution has already been dequeued; visibility-timeout expiry will resurface it and the poison guard caps redelivery — cancellation semantics are documented as "stop now; the run may retry once unless the item leaves the gate state".
- Every verb emits an audit event (structured log in the portal's own workspace: actor object id/UPN, verb, stack, item, timestamp, outcome).
*Alternative considered*: verbs call the stack's receiver via a new authenticated admin endpoint — rejected: adds an inbound admin surface to every per-client stack; the portal already has the mapping and the queue, so the stack needs no new API.

### D5 — AuthN/AuthZ: Entra ID with two app roles
One app registration (OpenID Connect); access requires an app role: `Portal.Reader` (all views) or `Portal.Operator` (views + verbs). Role assignment via Entra group membership, managed outside the portal. No anonymous surface except health probes. Internal tenant only — no B2B/B2C, no multi-tenant issuer.
*Alternative considered*: single role ("anyone signed in can do everything") — rejected: the verb path spends client agent-money and stops client runs; the split is cheap and makes the audit trail meaningful.

### D6 — Read credentials: portal gets its own least-privilege identities, never the stacks' write tokens
- **Azure**: the portal's managed identity is granted, per stack via Terraform: `Storage Queue Data Reader` + `Storage Queue Data Message Sender` (peek/depth + enqueue for verbs), ACA job read + execution-stop, and `Log Analytics Reader` on that stack's workspace.
- **Vendor**: reading gated items requires vendor API access. The portal uses **dedicated read-scoped credentials per stack**, stored in each stack's Key Vault alongside (not shared with) the agent's write credentials: GitHub — installation tokens minted with read-only permissions (the App supports permission-scoped minting); GitLab — a separate `read_api` token; Azure DevOps — a read-scoped PAT or the Entra service principal with read-only Work Items scope (aligns with the orchestrator's 5.1 spike outcome). The portal never reads the agent's write-capable secrets.
*Alternative considered*: reuse the agent's credentials (already in Key Vault) — rejected: the portal would concentrate write-capable tokens for every client in one always-on, internet-facing app; read-scoped duplicates cost one secret per stack and cut the blast radius of a portal compromise to read-only.

### D7 — Deployment: one central Container App, additive Terraform, portal optional by construction
The portal deploys as a single always-on Container App (min 1 replica — Blazor Server circuits are sticky; one replica is fine for an internal audience) in its own central resource group, with its own Terraform stack that references client stacks only through RBAC grants and `stacks.yaml`. Nothing in any client stack depends on the portal: if it is down or deleted, every loop runs untouched. Rollback = destroy/redeploy the portal alone.

### D8 — Mapping resolution becomes a Dispatch contracts assembly (refinement, found during implementation)
D1 says the portal "reuses Dispatch's mapping model for resolution", which the module boundary rules do not actually permit: MOD002 and the `Modules_Should_NotReferenceAnotherModuleImplementationDirectly` ArchTest allow one module to see another only through a `.Contracts` assembly, and `Orchestrator.Modules.Dispatch` has none — the mapping model, loader/validator, resolver, and queue contract all live in the implementation project. So the mapping types move to a new `Orchestrator.Modules.Dispatch.Contracts` (the webhook endpoint and the Azure queue client stay behind), and the Portal module consumes that.
*Alternative considered*: put the portal's read-model and verb logic directly in the host, where referencing module implementations is legal (the Receiver already does) — rejected: it would put substantial, test-worthy logic in a host the conventions define as a thin composer. *Alternative considered*: duplicate the mapping validator in the Portal — rejected outright: the mapping decides which states spend agent money, and two independently-drifting readers of an authorization surface is exactly the failure this indirection exists to prevent.

## Risks / Trade-offs

- [Portal concentrates cross-client read access in one internet-facing app] → Entra-gated at the front door (no anonymous pages), read-scoped vendor credentials only (D6), per-stack RBAC enumerated in Terraform, verbs behind a distinct role, full audit trail.
- [Log Analytics event schema drift breaks history views] → the dispatch-outcome event is a named contract in the orchestrator's observability behavior (spec delta there, additive); KQL queries live next to a schema-version constant and fail visibly in the UI, not silently.
- [Cross-workspace KQL limits (workspace count, query cost, throttling)] → per-source cache TTLs; queries scoped per stack view by default, cross-stack rollup capped and paginated; workspace count is bounded by client count (small).
- [Cancel is not exactly-once: the dequeued message can resurface after visibility timeout] → documented semantics ("may retry once"); poison guard bounds it; the human closes the loop by moving the item off the gate state — same contract as v1.
- [Blazor Server single replica = brief downtime on deploy] → accepted for an internal tool; loops are unaffected by portal downtime by construction (D7).
- [Read-scoped vendor tokens are still long-lived for GitLab/ADO] → same asymmetry already accepted in the orchestrator design (D6 there); shortest expiry + the same rotation runbook, one more entry per stack.

## Migration Plan

1. **Orchestrator side (additive, ships with the current change's stacks)**: add the normalized dispatch-outcome/enqueue `[LoggerMessage]` events; verify they land queryable in Log Analytics.
2. Scaffold `Orchestrator.Portal` host + Entra auth + `stacks.yaml` loader (fail-fast validation); deploy behind auth with a single stack registered.
3. Read-model in slices: queue/executions view → Log Analytics history view → vendor gate-items view (read-scoped credentials provisioned per D6).
4. Verbs (manual dispatch with live-gate check, cancel) + audit events; role split enforced.
5. Register remaining stacks (Terraform RBAC + `stacks.yaml` entries).
Rollback at any step: the portal is fully additive — remove the app; client stacks are untouched.

## Open Questions

- Whether the portal should later *offer* the vendor state transition as part of "retry" (write-back through adapters) instead of requiring the human to move the item on the board — deferred until the read-only portal proves the workflow.
- Stack registry auto-discovery via Azure tags once client count makes `stacks.yaml` tedious — revisit after ~5 stacks.
