# Tasks — add-architecture-design-docs

## 1. Ground truth

- [x] 1.1 Read the as-built sources — `terraform/*.tf` (names, replica counts, timeouts, secret names, queue name), `webhook-receiver/src`, `agent/src` entrypoint (env vars, failure trap, gate re-validation) — and note any place where `README.md` or `openspec/changes/ai-loop-aca-job/design.md` disagrees with the code (code wins).
- [x] 1.2 Skim the ds-connect reference docs for tone/structure (`~/projects/ds3d/ds-connect`: `ARCHITECTURE.md`, `ONBOARDING.md`, `AGENTS.md`, `CLAUDE.md`, `docs/context.md`, `docs/context-map.md`, `docs/template.md`, one or two ADRs). Adapt, never copy.

## 2. Context docs

- [x] 2.1 Write `docs/context.md` — why the stack exists, who it serves, domain in one glance: the `status:*` state machine, the two machine gates, the actors (humans with triage permission, the agent bot, reviewers).
- [x] 2.2 Write `docs/context-map.md` — neighbours and contracts per design D4: GitHub App/webhooks, target repos (`/ds:*` commands + single-label invariant + branch protection as the contract), github-runner pattern sibling (shared idioms, zero shared resources), Azure resources, LLM providers. Every edge states direction and payload.

## 3. ADRs

- [x] 3.1 Write `docs/adr/template.md` mirroring ds-connect's ADR template (Status/Date/Deciders/Tags, Context, Decision, Consequences, Alternatives).
- [x] 3.2 Write seed ADRs 0001–0005 from decisions D1–D5 of `openspec/changes/ai-loop-aca-job/design.md` (status Accepted, date 2026-07-24, real alternatives preserved; titles per the mapping table in this change's design.md).
- [x] 3.3 Write seed ADRs 0006–0009 from decisions D6–D9 (same rules).

## 4. Root docs

- [x] 4.1 Write `ARCHITECTURE.md` — big-picture diagram (move/adapt the README pipeline diagram), component map, load-bearing invariants, "where the code lives" table for every top-level directory, pointers to `docs/` and `openspec/`. Concrete values verified against task 1.1 notes.
- [x] 4.2 Write `DESIGN.md` — as-built design narrative: context, goals/non-goals, the human/agent loop diagram, one-paragraph decision summaries each linking to its ADR, risks/trade-offs (absorb README "Known trade-offs"). Leave the historical change design.md untouched.
- [x] 4.3 Write `ONBOARDING.md` — numbered path: orient (context, architecture) → get it running/verified (link README quick-start; absorb README "Rollout / verification" including the deliberate edge-case tests) → first contribution via the OpenSpec flow → where to get help (ADRs, AGENTS.md).
- [x] 4.4 Write `AGENTS.md` (tool-neutral entry: what the repo is, where things live, workflow, house rules) and `CLAUDE.md` (pointer-only to AGENTS.md, mirroring ds-connect's wording pattern).

## 5. README slim + coherence pass

- [x] 5.1 Slim `README.md` per the ownership table in design D5: keep intro summary + gate-label table pointer, quick-start (prerequisites, terraform usage, image builds); replace relocated sections with one-line pointers. No content lost, none duplicated.
- [x] 5.2 Coherence pass across the suite: every relative link resolves, each fact has exactly one owner, terminology consistent (gate label, single-label invariant, failure contract), and every spec scenario in `specs/project-documentation/spec.md` is satisfied.
