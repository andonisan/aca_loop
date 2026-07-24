# Proposal — Add architecture & design documentation suite

## Why

This repo runs production-adjacent infrastructure (an agent with repo-scoped write tokens, triggered by GitHub labels) but its only documentation is `README.md` plus the OpenSpec change history. Everything load-bearing — the invariants, the design decisions, the relationship to ds-connect and the github-runner stack — lives either in one long README or inside `openspec/changes/ai-loop-aca-job/design.md`, where decisions are frozen at proposal time and hard to discover. ds-connect (the primary consumer of this stack) already has a proven documentation suite (`ARCHITECTURE.md`, `DESIGN.md`, `ONBOARDING.md`, `docs/adr/`, `docs/context.md`, `docs/context-map.md`); this repo should mirror that convention so contributors and AI agents orient the same way in both repos.

## What Changes

- Add **`ARCHITECTURE.md`** — bird's-eye map: components (webhook receiver, queue, agent job, Terraform stack), the load-bearing invariants (single-label lock, gate re-validation, managed identity end to end, JIT tokens, no self-triggering), and where code lives. Follows the same "read this first, then follow pointers" convention as ds-connect's.
- Add **`DESIGN.md`** — the durable system-design narrative: context, goals/non-goals, the human/agent loop diagram, and decision summaries that link to the ADRs. (Unlike ds-connect, where DESIGN.md is a UI design contract, this repo has no UI — here it is the engineering design document.)
- Add **`ONBOARDING.md`** — zero to first contribution: orient via context docs, deploy/verify the stack, make a change through the OpenSpec flow.
- Add **`docs/context.md`** — why the stack exists, who it serves, the domain in one glance (the `status:*` label state machine, the two machine gates, the actors).
- Add **`docs/context-map.md`** — how this system relates to its neighbours: GitHub (App, webhooks, issues/PRs), the target repos (ds-connect's `/ds:*` commands and label lifecycle), the ds3d `github-runner` stack (pattern sibling, shares nothing), Azure resources, and LLM providers.
- Add **`docs/adr/`** — ADR template (mirroring ds-connect's `docs/template.md`) plus seed ADRs capturing the already-made decisions D1–D9 from `openspec/changes/ai-loop-aca-job/design.md`, recorded as **Accepted** with their real context and alternatives.
- Add **`AGENTS.md`** — the tool-neutral AI-agent entry point (what the repo is, where things live, workflow, house rules), mirroring ds-connect's convention.
- Add **`CLAUDE.md`** — a thin pointer to `AGENTS.md` (exists only because Claude Code looks for `CLAUDE.md` specifically; instructions live in one tool-neutral place).
- Slim **`README.md`** to quick-start + pointers, removing content that moves into the new docs (no information is lost, only relocated).

## Capabilities

### New Capabilities

- `project-documentation`: the repo's documentation suite — what documents must exist, what each must contain, how they cross-link, and how decisions are recorded as ADRs going forward.

### Modified Capabilities

<!-- none — no existing specs in openspec/specs/ -->

## Impact

- **New files**: `ARCHITECTURE.md`, `DESIGN.md`, `ONBOARDING.md`, `AGENTS.md`, `CLAUDE.md`, `docs/context.md`, `docs/context-map.md`, `docs/adr/template.md`, `docs/adr/0001…000N-*.md`.
- **Modified files**: `README.md` (slimmed, re-pointed).
- **No code, Terraform, or runtime behavior changes.** Documentation only.
- **Source material**: `README.md`, `openspec/changes/ai-loop-aca-job/{proposal,design,tasks}.md` and its specs; conventions borrowed from `~/projects/ds3d/ds-connect`.
