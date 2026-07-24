# Design — Architecture & design documentation suite

## Context

The repo has a single dense `README.md` and one archived-in-place OpenSpec change (`openspec/changes/ai-loop-aca-job/`) whose `design.md` holds the nine real design decisions (D1–D9). ds-connect — the repo this stack serves — has an established documentation convention this suite mirrors:

- `ARCHITECTURE.md` — orientation map + invariants + pointers ("read this first"), never restating behavioural specs.
- `DESIGN.md` — a top-level design contract (there: UI; here: system engineering design).
- `ONBOARDING.md` — numbered path from zero to first contribution, linking out instead of repeating.
- `docs/context.md` — the why/who/domain view; `docs/context-map.md` — bounded contexts and neighbours.
- `docs/adr/` — numbered ADRs following `docs/template.md` (Status/Date/Deciders/Tags, Context, Decision, Consequences, Alternatives).

Reference material lives at `~/projects/ds3d/ds-connect` (readable from this machine; content is adapted, never copied verbatim — the two systems differ).

## Goals / Non-Goals

**Goals:**
- A contributor (human or agent) can orient in this repo the same way they orient in ds-connect: same file names, same reading order, same conventions.
- Decisions D1–D9 become discoverable, individually referenceable ADRs with a template for future decisions.
- Each fact lives in exactly one place; everything else links to it. README shrinks to quick-start + pointers.
- Docs describe the system as built (Terraform, receiver, agent entrypoint) — verified against the code, not just against the old change docs.

**Non-Goals:**
- No code, Terraform, or behavior changes.
- No `CONTRIBUTING.md` in this pass (no established contributor workflow to codify yet; ADR process covers decision-making, `AGENTS.md` covers agent house rules).
- No restating of `openspec/changes/ai-loop-aca-job/specs/` content — docs link to specs, they don't duplicate them.
- No docs site/tooling (no generators, no lint) — plain markdown.

## Decisions

### D1: Mirror ds-connect's file layout exactly
Same names (`ARCHITECTURE.md`, `DESIGN.md`, `ONBOARDING.md`, `docs/context.md`, `docs/context-map.md`, `docs/adr/`, `docs/adr/template.md` — note ds-connect keeps the template at `docs/template.md`; we place it *inside* `docs/adr/` since this repo's `docs/` is otherwise small). One convention across the two repos beats a locally-optimal layout. *Alternative rejected*: a single `docs/README.md` hub — cheaper, but breaks the cross-repo muscle memory that motivates this change.

### D2: DESIGN.md is the engineering design narrative, not a UI contract
This repo has no UI. `DESIGN.md` carries what `openspec/changes/ai-loop-aca-job/design.md` pioneered — context, goals/non-goals, the human/agent loop diagram, risks/trade-offs — refreshed to as-built state, with each decision summarised in one paragraph linking to its ADR. The change's `design.md` stays untouched (it is a historical record).

### D3: Nine seed ADRs, one per existing decision
Map 1:1 from the change's decisions, recorded as **Accepted**, dated 2026-07-24, with real alternatives preserved:

| ADR | Title (short) | Source |
|---|---|---|
| 0001 | Webhook→queue→ACA Job over GitHub Actions workflows | D1 |
| 0002 | Machine-gate labels as the only trigger; no self-triggering | D2 |
| 0003 | Dedicated GitHub App, separate from the CI runner App | D3 |
| 0004 | Standalone stack with its own sibling receiver | D4 |
| 0005 | Execution-time gate re-validation as the concurrency lock | D5 |
| 0006 | Single agent image; runtime selected per message | D6 |
| 0007 | Failure contract: `status:blocked` + comment, no auto-retry | D7 |
| 0008 | Managed identity end to end; JIT GitHub tokens; secrets only in Key Vault | D8 |
| 0009 | Guardrails for untrusted issue content driving a write-capable agent | D9 |

*Alternative rejected*: fewer, merged ADRs (e.g. fold D2+D5 into one "concurrency & idempotency" record) — merging makes future supersession messy; one decision per record is the ADR norm.

### D3b: AGENTS.md is the single agent entry point; CLAUDE.md only points there
Mirror ds-connect exactly: `AGENTS.md` carries the tool-neutral agent instructions (what the repo is, where things live, the workflow, house rules — e.g. "docs are the deliverable here; never touch `terraform/` without an explicit ask"), and `CLAUDE.md` is a three-line pointer stating that instructions live in `AGENTS.md` and must not be added to `CLAUDE.md`. Keeps Claude Code, opencode, and Copilot in sync from one file. *Alternative rejected*: a full standalone CLAUDE.md — duplicates content across runtimes, which is exactly the drift this suite exists to prevent.

### D4: Context map is a systems map, not a DDD module map
ds-connect's context map shows internal bounded contexts. This repo is one context; its map shows **neighbours and contracts**: GitHub (App permissions, webhook events, issues/PRs), target repos (the `/ds:*` commands + `status:*` label state machine as the integration contract), the ds3d `github-runner` stack (pattern sibling — shared idioms, zero shared resources), Azure (RG, queue, Key Vault, ACA env), and LLM providers (Anthropic API / opencode providers). Each edge states direction and what crosses it.

### D5: README slims to quick-start + pointers; content relocates, never duplicates
Target ownership after the move:

| Content currently in README | New home |
|---|---|
| Intro + gate-label table | `docs/context.md` (README keeps a 3-sentence summary) |
| ASCII pipeline diagram | `ARCHITECTURE.md` |
| Layout section | `ARCHITECTURE.md` ("where the code lives") |
| Manual GitHub App prerequisites, usage, image builds | stays in README (it *is* quick-start) |
| Rollout / verification | `ONBOARDING.md` ("get it running / verify") |
| Known trade-offs | `DESIGN.md` (risks/trade-offs) |

### D6: Verify against code before writing
Each doc's claims are checked against `terraform/*.tf`, `webhook-receiver/src`, and `agent/src` (e.g. actual env var names, `min_replicas`, timeout values, Key Vault secret names) so the suite documents the as-built system, not the proposal-time intent. Where code and old docs disagree, code wins and the discrepancy is noted in the task output.

## Risks / Trade-offs

- **[Docs drift]** → mitigated by the one-place-per-fact rule and by pointing behavioural truth at `openspec/` specs; accepted residual risk, same as ds-connect.
- **[Adapting ds-connect prose too literally]** → the systems differ (monolith app vs event-driven infra); mitigation: D4/D2 explicitly redefine what DESIGN.md and context-map mean here.
- **[ds-connect repo unavailable at implementation time]** → the conventions are captured in this design; implementation does not require re-reading ds-connect, only benefits from it.

## Migration Plan

Pure addition + README slimming in one PR; no rollout concerns. Order: context docs → ARCHITECTURE → ADRs → DESIGN → ONBOARDING → AGENTS.md + CLAUDE.md → README slim (README last, so pointers resolve).

## Open Questions

- None blocking. (Whether to later add `CONTRIBUTING.md`/`AGENTS.md` is deferred — see Non-Goals.)
