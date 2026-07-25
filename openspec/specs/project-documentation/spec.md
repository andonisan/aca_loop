# project-documentation Specification

## Purpose
TBD - created from change `add-architecture-design-docs`. Update Purpose after archive.

## Requirements

### Requirement: Architecture orientation document
The repo SHALL have an `ARCHITECTURE.md` at the root that maps the system: every deployed component (webhook receiver, storage queue, agent Container Apps Job, and their Terraform sources), the load-bearing invariants (gate-label trigger filter, execution-time gate re-validation as the lock, no self-triggering, managed identity end to end, JIT-minted GitHub tokens, secrets only in Key Vault), and a "where the code lives" table covering every top-level directory. It MUST link to deeper docs rather than restating them, and MUST NOT restate behavioural content owned by `openspec/` specs.

#### Scenario: New contributor orients from ARCHITECTURE.md
- **WHEN** a reader opens `ARCHITECTURE.md` knowing nothing about the repo
- **THEN** they can name each deployed component, state each load-bearing invariant, and locate the directory that owns any given concern, following at most one link per question

#### Scenario: Claims match the code
- **WHEN** `ARCHITECTURE.md` states a concrete value (replica counts, timeout defaults, queue name, secret names)
- **THEN** that value matches the current `terraform/`, `webhook-receiver/`, or `agent/` sources

### Requirement: Engineering design document
The repo SHALL have a `DESIGN.md` at the root containing the durable system-design narrative: context, goals and non-goals, the human/agent label-loop diagram, known risks and trade-offs, and a one-paragraph summary of each architectural decision linking to its ADR. It MUST reflect the as-built system rather than proposal-time intent, and MUST NOT modify the historical `openspec/changes/ai-loop-aca-job/design.md`.

#### Scenario: Decision summaries resolve to ADRs
- **WHEN** a reader follows any decision summary in `DESIGN.md`
- **THEN** they land on an ADR in `docs/adr/` that records that decision's context, alternatives, and consequences

### Requirement: Onboarding guide
The repo SHALL have an `ONBOARDING.md` at the root that takes a contributor from zero to a first contribution as a numbered path: orient (context docs, architecture), get the stack running and verified (deploy prerequisites, rollout verification, deliberate edge-case tests), and make a change through the OpenSpec flow. It MUST link to the owning documents instead of repeating their content.

#### Scenario: Onboarding path has no dead ends
- **WHEN** a new contributor follows `ONBOARDING.md` top to bottom
- **THEN** every step either contains its instructions or links to a document that does, and every relative link resolves to an existing file

### Requirement: Context and context-map documents
The repo SHALL have `docs/context.md` (why the stack exists, who it serves, and the domain in one glance: the `status:*` label state machine, the two machine gates, and the actors) and `docs/context-map.md` (a systems map of neighbours — GitHub App and webhooks, target repos with the `/ds:*` command and label contract, the ds3d `github-runner` pattern sibling, Azure resources, LLM providers). Each edge in the context map MUST state its direction and what crosses it.

#### Scenario: Integration contract is discoverable
- **WHEN** a reader asks "what does this stack assume about a target repo?"
- **THEN** `docs/context-map.md` answers it: the gate labels, the single-label invariant, the `/ds:propose` and `/ds:implement` commands, and required branch protection

### Requirement: Architecture decision records
The repo SHALL have a `docs/adr/` directory with a numbered-ADR template (`docs/adr/template.md` with Status, Date, Deciders, Tags, Context, Decision, Consequences, and Alternatives sections) and one seed ADR per decision D1–D9 from `openspec/changes/ai-loop-aca-job/design.md`, each recorded as Accepted with its real context and rejected alternatives. New architectural decisions MUST be recorded as new ADRs; superseding an ADR MUST set the old one's status to "Superseded by ADR-NNNN" rather than editing its decision.

#### Scenario: Past decision is auditable
- **WHEN** a contributor questions why the stack uses a queue-triggered ACA Job instead of GitHub Actions workflows
- **THEN** a seed ADR states the decision, the alternative considered, and why it was rejected

#### Scenario: Decision is superseded
- **WHEN** a later change reverses a recorded decision
- **THEN** a new ADR is added and the old ADR's status becomes "Superseded by" the new one, with its original content intact

### Requirement: Tool-neutral agent entry point
The repo SHALL have an `AGENTS.md` at the root as the single AI-agent entry point (what the repo is, where things live, the contribution workflow, house rules) and a `CLAUDE.md` whose only content is a pointer to `AGENTS.md`. Agent instructions MUST NOT be added to `CLAUDE.md`.

#### Scenario: Any agent runtime gets the same instructions
- **WHEN** an agent runtime loads its convention file (`CLAUDE.md` for Claude Code, `AGENTS.md` for others)
- **THEN** it ends up reading the instructions in `AGENTS.md`, and no instruction exists only in `CLAUDE.md`

### Requirement: Single source of truth across docs
Each fact SHALL live in exactly one document, with other documents linking to it: README owns quick-start (prerequisites, Terraform usage, image builds), `docs/context.md` owns the why/domain, `ARCHITECTURE.md` owns the component map and invariants, `DESIGN.md` owns rationale and trade-offs, `ONBOARDING.md` owns the contributor path, and `openspec/` owns behavioural specs.

#### Scenario: Relocated README content is not duplicated
- **WHEN** content moves out of `README.md` into the new documents
- **THEN** `README.md` retains at most a summary plus a link, and no section appears in full in two places
