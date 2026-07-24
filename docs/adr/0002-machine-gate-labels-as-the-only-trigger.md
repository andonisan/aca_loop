# ADR-0002: Machine-gate labels are the only trigger, and the loop cannot trigger itself

- **Status:** Accepted
- **Date:** 2026-07-24
- **Deciders:** Andoni Santamaria
- **Tags:** infra, github, agent

## Context

The receiver sees every `issues` event for every repo the App is installed on. The agent itself writes labels as part of its work (`status:proposal-review`, `status:in-progress`, `status:code-review`, `status:blocked`), so a naive "any label change dispatches an agent" rule would recurse. GitHub also redelivers webhooks, and humans re-triage issues while work is queued.

## Decision

We will enqueue only deliveries where `action == "labeled"` **and** the applied label is one of the two machine gates (`status:ready-for-proposal`, `status:ready-for-implementation`; configurable via `GATE_LABELS`). Because the agent's own label writes are never gate labels, the automation cannot re-trigger itself structurally; as belt-and-braces, deliveries whose `sender` is this automation's bot (`AGENT_BOT_LOGIN`) are dropped too. Duplicate deliveries are neutralized at execution time by gate re-validation ([ADR-0005](./0005-execution-time-gate-re-validation-as-the-concurrency-lock.md)), not by receiver-side state.

## Consequences

- **Positive:** the receiver stays stateless and trivially auditable; recursion is impossible by construction, not by bookkeeping; adding a gate is a Terraform variable change.
- **Negative:** any future "AI on other labels" idea must revisit the recursion analysis before widening the filter.
- **Neutral:** duplicate deliveries still consume a job execution that exits clean at re-validation — accepted noise.

## Alternatives considered

- **Receiver-side dedup/state (remember processed deliveries)** — rejected: introduces storage and failure modes for a problem the single-label invariant already solves at execution time.
- **Trigger on any `status:*` change and decide in the job** — rejected: spends a job execution (and cold start) on every lifecycle transition, and moves the recursion risk into more code.

## References

- Source decision: D2 in `openspec/changes/ai-loop-aca-job/design.md`
- Related: [ADR-0005](./0005-execution-time-gate-re-validation-as-the-concurrency-lock.md)
