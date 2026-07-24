# ADR-0005: Execution-time gate re-validation is the concurrency lock

- **Status:** Accepted
- **Date:** 2026-07-24
- **Deciders:** Andoni Santamaria
- **Tags:** agent, correctness

## Context

Between enqueue and execution, the world can change: GitHub redelivers webhooks, humans re-triage issues, KEDA overscales (more executions than queued items), and two deliveries for the same issue can race. Classic answers are distributed locks or receiver-side dedup state. But target repos already enforce a **single-label invariant** — an issue has exactly one `status:*` label — and the agent's own command flips that label as its first durable effect.

## Decision

We will re-validate the gate at execution time and treat the label itself as the lock. After dequeuing, the entrypoint re-reads the live issue; if the gate label is gone — another execution took it, a human moved it, or this is a duplicate delivery — it exits 0 and deletes the message. Empty dequeues (KEDA overscale) exit 0 before contacting GitHub or any LLM provider. No distributed-lock machinery exists anywhere.

## Consequences

- **Positive:** duplicate deliveries, overscale, and human re-triage all collapse into one cheap, stateless check; the race window shrinks to the seconds between re-check and the command's own label flip.
- **Negative:** that residual window is real — two executions grabbing the same issue within seconds could both start; accepted because the command's label flip makes the second one's work visibly redundant, not corrupting.
- **Neutral:** "exit 0 on nothing-to-do" is the same philosophy as the runner stack's empty-queue check; operators must not read clean exits as failures.

## Alternatives considered

- **Distributed lock (blob lease, etc.)** — rejected: new failure modes (lease expiry vs hour-long runs) to close a seconds-wide window the invariant already covers.
- **Receiver-side dedup** — rejected: stateful receiver for the same residual race ([ADR-0002](./0002-machine-gate-labels-as-the-only-trigger.md) keeps the receiver stateless).

## References

- Source decision: D5 in `openspec/changes/ai-loop-aca-job/design.md`
- Related: [ADR-0002](./0002-machine-gate-labels-as-the-only-trigger.md), [ADR-0007](./0007-failure-contract-status-blocked-plus-comment-no-auto-retry.md)
