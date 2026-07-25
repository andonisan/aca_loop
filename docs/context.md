# Context

Why this stack exists and who it serves — the product/domain orientation. For *how it's built*, see [`ARCHITECTURE.md`](../ARCHITECTURE.md); for how it relates to its neighbours, see [`context-map.md`](./context-map.md).

## The problem

ds-connect works spec-first through a nine-state issue lifecycle driven by `status:*` labels. Two of those transitions are **machine gates**: the work behind them (`/ds:propose`, `/ds:implement`) is fully specified by the repo's own command layer and needs no human at the keyboard — yet someone still had to notice the label, open a terminal, and launch an agent by hand. This stack removes that launch step: when an issue reaches a machine-gate label, a cloud agent picks it up, runs the repo's own command headlessly, and hands the result back to the humans exactly where the process already puts them (spec review, code review).

## Who it serves

- **Maintainers of target repos** (ds-connect first) — they dispatch agent work by applying a label and receive it back as a draft PR or an implemented PR.
- **Reviewers** — the loop is closed by them: approving a spec advances the label to the next gate; requesting changes and re-applying a gate label re-dispatches the agent, which then sees the review comments.
- **Operators of this stack** — whoever deploys and tunes the Azure infrastructure in this repo.

The stack is deliberately repo-agnostic: it assumes only the label/command contract described in [`context-map.md`](./context-map.md), not ds-connect internals.

## Domain in one glance

- **Issue lifecycle** — nine `status:*` labels form a state machine (`backlog → needs-refinement → ready-for-proposal → proposal-review → ready-for-implementation → in-progress → code-review → done`, plus `blocked` reachable from anywhere), owned by the target repo's process (ds-connect: `CONTRIBUTING.md`).
- **Single-label invariant** — an issue carries exactly one `status:*` label at a time. This is load-bearing here: the label moving *is* the lock ([ADR-0005](./adr/0005-execution-time-gate-re-validation-as-the-concurrency-lock.md)).
- **Machine gate** — one of the two labels that dispatch an agent: `status:ready-for-proposal` (→ `/ds:propose`) and `status:ready-for-implementation` (→ `/ds:implement`). No other label triggers anything.
- **Work item** — the queue message `{repo, issue_number, gate_label, agent_runtime, enqueued_at}`; one work item = one agent job execution.
- **Agent runtime** — the CLI that runs the command: Claude Code (`claude`) or opencode, chosen per work item ([ADR-0006](./adr/0006-single-agent-image-runtime-selected-per-message.md)).
- **Failure contract** — a dead or timed-out run flips the issue to `status:blocked` with an explanatory comment; a human re-labels to retry ([ADR-0007](./adr/0007-failure-contract-status-blocked-plus-comment-no-auto-retry.md)).

## Actors

| Actor | Role in the loop |
| --- | --- |
| **Humans with triage permission** | The authorization gate: applying a gate label is what spends agent time. Branch protection and label triage are the two things a target repo must enforce. |
| **The agent bot** (`<app-slug>[bot]`) | The GitHub App identity all agent writes happen under. Its own label events are dropped by the receiver so the loop can't trigger itself. |
| **Reviewers** | Close the loop through GitHub review; never interact with this stack directly. |
| **The agent itself** | Ephemeral: one Container Apps Job execution per work item, holding a repo-scoped token that expires in ≤1 hour. |
