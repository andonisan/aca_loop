# ADR-0007: Failure contract — `status:blocked` plus a comment, never automatic retry

- **Status:** Accepted
- **Date:** 2026-07-24
- **Deciders:** Andoni Santamaria
- **Tags:** agent, correctness

## Context

A failed agent run is not idempotent-retryable the way a CI job is: it may have left a partial branch, a half-written draft PR, or spent real LLM budget. Silently requeueing would re-spend that budget against an unknown intermediate state. Meanwhile the board must never lie — an issue whose agent died must not sit at a gate label looking like it's still queued.

## Decision

We will make failure a first-class, human-visible state transition. The entrypoint traps failure and timeout (a watchdog fires 120 s before the platform's replica timeout so the trap can run): it flips the issue's `status:*` label to `status:blocked` (preserving the single-label invariant and non-status labels), comments the reason plus a log tail, and deletes the message. A human re-labels to retry. Two deliberate exceptions:

- **WIP-limit refusal** from `/ds:implement` (output matching `WIP_REFUSAL_PATTERN`) is not a failure: the gate label is kept, a comment explains, the message is deleted.
- **Entrypoint-level crashes** before the trap is armed do retry, via the queue's visibility timeout, capped by the poison guard (3 dequeues) and `replica_retry_limit = 0` at the Azure layer.

## Consequences

- **Positive:** the board always reflects reality; failures carry their evidence (log tail, links to partial work); no runaway retry spend.
- **Negative:** every real failure costs a human touch to retry; partial branches/PRs are left as residue for manual cleanup.
- **Neutral:** a scheduled sweep re-dispatching WIP-refused issues when capacity frees is possible future work.

## Alternatives considered

- **Requeue on failure (CI-style retry)** — rejected: re-spends LLM budget against non-idempotent partial state.
- **Fail silently (delete message, no label change)** — rejected: the issue would sit at a gate label forever, looking queued while nothing is coming.

## References

- Source decision: D7 in `openspec/changes/ai-loop-aca-job/design.md`
- Related: [ADR-0005](./0005-execution-time-gate-re-validation-as-the-concurrency-lock.md)
- Implementation: `agent/src/entrypoint.js` (watchdog, WIP pattern, poison guard)
