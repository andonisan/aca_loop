# Design — Agentic Backlog Orchestrator

## Context

The v1 stack in this repo (`ai-loop-aca-job`, deployed and verified) implements the full loop for exactly one shape of the problem: GitHub issues, two hardcoded gate labels, two hardcoded `/ds:*` commands, one client repo. Its load-bearing pieces are already general in spirit:

- **Dispatch machine**: always-on webhook receiver → HMAC verify → filter → Storage Queue → KEDA-scaled ACA Job ("AI pod"), scale 0..N, one message per execution, poison-message guard, delete-on-success.
- **Agent contract** (`agent/src/entrypoint.js`): dequeue → re-validate the gate on the live item → mint short-lived credentials → clone → run the repo's own command headlessly → on failure flip the item to "blocked" + comment → delete message.
- **Multi-runtime image** (`agent/Dockerfile`): Claude Code, opencode, and `gh` already coexist; runtime is chosen per message today (`agent_runtime` field), but the choice is plumbing, not product config.

What is *not* general: vendor (GitHub only, `github.js` in both services), the state model (label strings compared verbatim), the state→action mapping (an `if` on two labels), and the tenancy model (one repo, one App installation).

The product brief ([`DOCS/products/orquestador-agentico-backlog.md`](../../../DOCS/products/orquestador-agentico-backlog.md)) asks for: Azure DevOps + GitHub + GitLab, a configurable state→action engine, runtime selection as configuration, same AI-pod dispatch on Azure Container Apps.

## Goals / Non-Goals

**Goals:**
- A canonical work-item/state model with one adapter per vendor (GitHub, Azure DevOps, GitLab), covering inbound event normalization and outbound state write-back (including failure→blocked).
- A declarative per-repo/client mapping: canonical state → agent command + runtime, consumed by both the receiver (filter/dispatch) and the agent (gate re-validation).
- Vendor-agnostic queue message; the ACA Jobs dispatch pattern of v1 preserved unchanged in semantics.
- Runtime selection (claude | opencode) surfaced as mapping configuration.

**Non-Goals:**
- No AKS/Kubernetes port — the dispatcher stays on Azure Container Apps Jobs.
- No orchestrator-owned workflow engine: the vendor's board remains the state machine and its single-state invariant remains the lock. We map states; we do not define lifecycles.
- No Copilot CLI runtime (same v1 rationale: user-subscription auth).
- No UI/portal in this change; configuration is a versioned file.
- No GitHub Projects / Azure Boards *board-field* integration — state is labels (GitHub/GitLab) or work item state (Azure DevOps), never board columns.

## Decisions

### D1 — Canonical event, adapters at the edge
One receiver hosts three vendor adapters. Each adapter owns: webhook signature verification (GitHub HMAC-SHA256, GitLab secret token, Azure DevOps basic-auth/subscription secret), event parsing, bot-identity self-filtering, and normalization to a canonical event `{vendor, project, repo, item_id, native_state, actor}`. Everything downstream of the adapter is vendor-blind.
*Alternative considered*: one receiver app per vendor — rejected: triples the always-on cost (`min_replicas=1` each) for no isolation benefit; adapters are stateless parsers.

### D2 — Mapping config is a file in the orchestrator's deployment, per target
A YAML mapping per target (`targets[]`: vendor, repo/project coordinates, state map `native_state → {action, runtime, on_success_state, on_failure_state}`). Deployed with the orchestrator (baked into config or mounted), not read from the target repo at dispatch time.
*Rationale*: the receiver must filter events synchronously without cloning anything; and the mapping is an *authorization* surface (which states spend agent money) so it must not be writable by whoever can push to the target repo.
*Alternative considered*: config in the target repo (like v1's implicit contract) — rejected for the receiver path, but the target repo still owns the *commands themselves* (`.claude/commands`), preserving "no agent logic in this infra".

### D3 — Queue message becomes canonical (breaking, internal)
`{vendor, project, repo, item_id, state, action, runtime, on_failure_state}` replaces `{repo, issue_number, gate_label, agent_runtime}`. `on_failure_state` is resolved by Dispatch from the mapping and travels on the message rather than being re-resolved (or independently configured) on the Agent side — the Agent never loads the mapping at all, it only trusts what Dispatch already validated (implemented: tasks.md 2.4). `on_success_state` stays mapping-only (not on the message) since no vendor today needs an infra-driven success write-back (see D5). Receiver and agent entrypoint change in lockstep; the queue is drained/idle during deploy so no compatibility window is needed.

### D4 — Gate re-validation goes through the adapter
The agent re-reads the live item via the vendor adapter (as a library shared between receiver and agent, or duplicated per service as in v1 — shared library preferred now that there are three vendors) and exits 0 if the item no longer carries the mapped state. The invariant generalizes: GitHub/GitLab = exactly one lifecycle label; Azure DevOps = the work item's State/column field, which is single-valued by construction.

### D5 — Write-back and failure contract per vendor
On failure the agent moves the item to `on_failure_state` (carried on the message, D3) + a comment with the log tail. Success write-back is *not* infra's job for any vendor implemented so far — GitHub's `/ds:propose`/`/ds:implement` do their own label flip via `gh` inside the running agent session (v1 behavior, preserved) — so `on_success_state` stays a mapping-file-only field today, parsed and validated but not wired to any runtime behavior; a vendor whose own commands can't self-report success would consume it later. GitHub: failure write-back is swap labels + issue comment. GitLab: swap labels + note. Azure DevOps: set work item State (or a designated field if the process template lacks a "Blocked" state — config decides the target value) + discussion comment.

### D6 — Credentials per vendor, JIT where the vendor allows
GitHub keeps the App + per-repo installation-token minting (v1). GitLab: project access token (or group token) from Key Vault — GitLab has no App-equivalent JIT minting; scope to the minimum (api, write_repository) per project. Azure DevOps: PAT or service principal (Entra ID) from Key Vault; prefer Entra service principal since the stack is already managed-identity-first. All read at execution time from Key Vault via managed identity; nothing static in images or Terraform state.

### D7 — Backend: .NET Aspire modular monolith, organized like ds-connect
The product is a .NET solution mirroring ds-connect's organization (its `ARCHITECTURE.md` conventions): `src/` as the .NET root (`.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`); `src/root/` for hosts; `src/shared/` for the kernel (`BuildingBlocks`, `Constants`, `ServiceDefaults`, `ArchitectureAnalyzers` with the MOD analyzer set); `src/modules/<Module>/` for feature modules; `src/tests/` mirroring modules plus `ArchTests` and `EndToEndTests`.

Hosts (thin composers, like `DsConnect.Server`): `Orchestrator.Receiver` (minimal API, the always-on Container App), `Orchestrator.Agent` (console worker, the ACA Job entrypoint), `Orchestrator.Aspire` (AppHost: receiver + agent + Azurite for the queue locally). Two hosts instead of ds-connect's single BFF is the one structural divergence.

Modules (per ADR-0002's "module only at a real seam" test): `Backlog.Contracts` (canonical event/message, `IVendorAdapter`); one module per vendor (`GitHub`, `GitLab`, `AzureDevOps`) — vendors are a real seam (added per client, own credentials and lifecycle) but need no own `.Contracts`: they implement the shared `IVendorAdapter` and self-register via `IModule`, keyed by vendor; `Dispatch` (mapping-config, filter, enqueue — consumed by Receiver); `Execution` (gate re-validation, runtime run with `Process` + `CancellationTokenSource` watchdog, failure contract — consumed by Agent). Use cases are single-file vertical slices under `Features/<Feature>/UseCases`. No persistence layer at all — the orchestrator is stateless (state lives in the vendor and the queue), so ds-connect's schema-per-module invariant doesn't apply; the MOD cross-module rules and ArchTests do, from day one.

Aspire is used for local composition (Azurite; Key Vault + Storage client integrations over `DefaultAzureCredential` so local and ACA code paths are identical) and service defaults (OTel, health checks). **Deployment stays on the existing Terraform stack** — no `azd`/Aspire provisioning.
*Alternative considered*: evolve the v1 Node.js services — rejected: fine for the v1 spike, but the product needs typed contracts across three adapters, mechanically-enforced module boundaries, and the team's stack and conventions are already this exact shape in ds-connect.

**Adopted spec baseline from ds-connect** (`~/projects/ds3d/ds-connect/openspec/specs/`) — these existing specs are ported/adapted rather than re-invented, renaming `DsConnect.*` → `Orchestrator.*`:
- Near-verbatim: `repo-structure` (solution at `src/`, `Directory.Build.props` with warnings-as-errors, Central Package Management with transitive pinning, enforced `.editorconfig`), `module-boundary-analyzers` (MOD001–005 + CQS001 auto-attached, ArchTests incl. duplicate-LoggerMessage-EventId test), `observability` (shared `ActivitySource`, `[LoggerMessage]` structured logging, OTel via ServiceDefaults, env-driven exporters, `/api/health` + `/api/alive` — used by ACA probes), `commit-hooks` / `commit-message-convention` / `pre-commit-checks` / `code-comment-policy`.
- Adapted (drop persistence/EF/domain-events/caching — the orchestrator is stateless): `backend-architecture` (`IModule`/`ModuleBase` self-registration, `Features/<F>/UseCases` slices, `IUseCase` minimal APIs, CQS decorator pipeline, domain errors as `ErrorOr<T>` values — which the agent failure contract maps onto), `shared-building-blocks` + `building-blocks-behaviors` (kernel keeping `API`, `CQS`, `Diagnostics`, `Modules`, `Constants`), `api-edge` (ProblemDetails + `ErrorOr`→HTTP + OpenAPI/Scalar dev-only, Receiver-side), `testing-strategy` (xUnit + Shouldly, per-module Unit/Functional tests, shared Testcontainers fixture — with **Azurite** replacing Postgres/Redis).
- Referenced as the automated contract, not ported: `issue-lifecycle` and `workflow-commands` are the formal spec of the workflow the orchestrator automates (machine gates, draft-PR/single-PR invariants, WIP limit, idempotent label provisioning) — the authoritative source for the reference mapping config, alongside CONTRIBUTING.md.

### D8 — Runtime selection stays on one image
Keep the single multi-runtime image; the mapping's `runtime` field selects claude vs opencode per action (and `gh` remains tooling available to both, not a runtime). *Alternative*: one image per runtime — rejected: the v1 image already proves coexistence, and a single image keeps the Job definition static while the choice stays per-message.

## Risks / Trade-offs

- [Azure DevOps state model varies per process template (Agile/Scrum/CMMI/custom)] → the mapping config maps *named states*, never assumes a template; validation tooling checks the configured states exist on the target project at deploy time.
- [Three webhook auth schemes, three credential types] → each adapter isolates its scheme; secrets one-per-vendor-per-target in Key Vault; the threat model note from v1 (untrusted item bodies driving a write-capable agent) now applies per vendor and belongs in each adapter's spec.
- [Shared mapping config becomes a single point of misconfiguration] → schema-validate on receiver startup (fail fast, refuse to serve), not per event.
- [GitLab/Azure DevOps tokens are longer-lived than GitHub installation tokens] → shortest supported expiry + rotation runbook; documented as a known asymmetry rather than papered over.
- [Breaking queue schema] → deploy receiver+agent together with the queue drained; poison-guard already caps damage from any stray old-format message.

## Migration Plan

1. Land the shared adapter library + canonical message behind the existing GitHub-only behavior (v1 parity, GitHub adapter extracted — no functional change).
2. Introduce the mapping config and replace the hardcoded label→command `if`; ship with a config equivalent to v1's two gates (still GitHub-only). Verify against the v1 rollout checklist.
3. Add GitLab adapter + credentials; pilot on a scratch GitLab project.
4. Add Azure DevOps adapter + credentials; pilot on a scratch ADO project.
Rollback at any step = redeploy previous images; Terraform changes are additive (new secrets/vars) until step 3.

## Open Questions

- ~~Multi-tenancy: one ACA environment for all clients vs. one stack per client~~ **Resolved (2026-07-24): one stack per client** — independent blast radius wins; config decides targets within each stack. Cross-client visibility is not this change's problem: it belongs to a central, internal-only web control plane layered *above* the stacks (see the `orchestrator-portal` change proposal).
- ~~Azure DevOps auth: PAT vs. Entra service principal~~ **Resolved (2026-07-24): PAT for this implementation.** A live spike against the Work Items + Repos REST APIs with an Entra service principal needs a real Azure DevOps org and wasn't possible in the sandbox this was built in — same live-verification gap as the GitLab/ADO adapter pilots (tasks.md 4.4/5.5). PAT is what `AzureDevOpsAdapter` implements today: HTTP Basic auth with an empty username and the PAT as password, read from Key Vault via managed identity like every other vendor secret (never a static credential, never in Terraform state). Migrating to an Entra service principal later is a same-shape swap inside `AzureDevOpsAdapter`'s auth header construction — nothing in the canonical contracts (`CanonicalEvent`/`CanonicalMessage`/`IVendorAdapter`) needs to change for it.
- ~~Whether WIP limits should also have an orchestrator-side guard per target~~ **Resolved (2026-07-24): no.** Kept as v1 had it: the target repo's own command (`/ds:implement`) is the sole WIP enforcer, detected by the Agent via `ExecutionOptions.WipRefusalPattern` matching the command's own refusal output — never re-implemented against vendor state. Adding a second, orchestrator-side counter would mean querying "how many items are currently in-progress" per target across three different vendor state models, for a guard the target already owns; not worth the complexity unless a target's own command turns out not to self-enforce.
