# Design

The as-built engineering design of the AI loop — context, goals, the loop itself, the decisions and their trade-offs. For the component map read [`ARCHITECTURE.md`](./ARCHITECTURE.md) first; each decision below is recorded in full (context, alternatives, consequences) in [`docs/adr/`](./docs/adr/). The founding OpenSpec change ([`openspec/changes/ai-loop-aca-job/`](./openspec/changes/ai-loop-aca-job/)) is the historical record of how this design emerged; this file is the current state.

## Context

Two systems anchor the design:

1. **The ds3d `github-runner` stack** — the proven event→queue→ephemeral-container machine: webhook receiver verifies HMAC and enqueues; a KEDA-scaled Container Apps Job drains one message per execution with scale-to-zero, managed identity throughout, JIT credentials, poison-message guards.
2. **The target repos' issue lifecycle** (ds-connect first) — nine `status:*` labels with a single-label invariant, where two transitions are machine gates: `ready-for-proposal` → `/ds:propose` and `ready-for-implementation` → `/ds:implement`. Commands are runtime-portable (Claude Code and opencode read the same files).

## The loop

Humans stay exactly where the process already puts them; the loop is closed by moving labels:

```
                  HUMAN                        ACA AGENT
  backlog ──/ds:grill─┐
      ▼               │
  ready-for-proposal ───── webhook ───▶ /ds:propose (draft PR)
      ▼                                       │
  proposal-review ◀── label flipped by command┘
      ├─ approve → ready-for-implementation ── webhook ──▶ /ds:implement
      └─ revise  → ready-for-proposal (re-trigger = THE LOOP)
      ...
  code-review → human /ds:sync → done
```

Requesting changes and re-applying a gate label re-dispatches the agent, which then sees the reviewer's PR comments — that re-trigger *is* the feedback loop.

## Goals / Non-Goals

**Goals:**
- Issues reaching a machine-gate label get worked by a cloud agent with no human launch step.
- Scale to zero; pay per agent run.
- The agent runs the target repo's own commands — no agent logic in this infra beyond the entrypoint contract.
- Runtime (Claude Code vs opencode) selectable per run for cost/quality comparison.
- Failure never leaves the board lying.

**Non-Goals:**
- Copilot CLI support (its headless auth is tied to a user subscription; GitHub's hosted Copilot coding agent is the native alternative).
- Automating the human stages (`/ds:grill`, spec review, code review, `/ds:sync`).
- A generic "AI on any label" platform — the trigger is exactly the two machine gates.
- Autonomous iterate-until-CI-green loops — future work.

## Decisions

Each summary links to the full ADR — read those before changing anything they cover.

- **[ADR-0001](./docs/adr/0001-webhook-queue-aca-job-over-github-actions-workflows.md) — Webhook→queue→ACA Job, not GitHub Actions.** Actions workflows would be per-repo, run on the CI image, and inherit CI-shaped timeouts; the queue path is org-wide via one App install and reuses the proven runner pattern.
- **[ADR-0002](./docs/adr/0002-machine-gate-labels-as-the-only-trigger.md) — Gate labels are the only trigger; no self-triggering.** The agent's own label writes are never gate labels, so recursion is impossible by construction; the bot-sender drop is belt-and-braces; the receiver stays stateless.
- **[ADR-0003](./docs/adr/0003-dedicated-github-app-separate-from-ci-runner-app.md) — A dedicated GitHub App.** Two narrow Apps (CI: Administration; this: Issues/Contents/PRs) beat one broad one: independent blast radius, revocation, audit.
- **[ADR-0004](./docs/adr/0004-standalone-stack-with-sibling-receiver.md) — Standalone stack, sibling receiver.** Revised from "extend the runner's receiver" at apply time: full repo independence for the price of one extra always-on replica.
- **[ADR-0005](./docs/adr/0005-execution-time-gate-re-validation-as-the-concurrency-lock.md) — Gate re-validation is the lock.** The single-label invariant makes the label flip the effective lock; a live re-check at execution start shrinks the race window to seconds with zero lock machinery.
- **[ADR-0006](./docs/adr/0006-single-agent-image-runtime-selected-per-message.md) — One fat agent image, runtime per work item.** Both CLIs read the same command files; which runs is config (`agent_runtime` in the message, defaulted by Terraform), not an image variant.
- **[ADR-0007](./docs/adr/0007-failure-contract-status-blocked-plus-comment-no-auto-retry.md) — Failure contract.** Trap + watchdog flip the issue to `status:blocked` with a log tail; agent work is never auto-retried; WIP-limit refusals keep the gate label; only pre-trap crashes retry (poison-guarded).
- **[ADR-0008](./docs/adr/0008-managed-identity-jit-tokens-secrets-only-in-key-vault.md) — Identity and secrets.** Managed identity for every Azure edge, least-privilege RBAC per identity, JIT single-repo GitHub tokens (≤1 h, never stored), secrets only in Key Vault with the sensitive ones pushed out-of-band.
- **[ADR-0009](./docs/adr/0009-guardrails-for-untrusted-issue-content.md) — Guardrails on untrusted input.** Prompt injection is bounded structurally: triage-permission labeling as authorization, token scope, branch protection, turn/time caps, allowed-tools policy.

## Risks / Trade-offs

- **Always-on receiver.** `min_replicas=1` plus Storage/Key Vault/Log Analytics is a small fixed cost; everything else bills per execution ([ADR-0004](./docs/adr/0004-standalone-stack-with-sibling-receiver.md)).
- **Agent runs are long.** `replica_timeout_seconds` (default 3600) is a hard per-run ceiling; the entrypoint's watchdog fires 120 s earlier so the failure contract runs before the platform kills the replica. LLM spend per run is bounded by `agent_max_turns`.
- **No automatic retry of agent work.** A failed run goes to `status:blocked` and a human re-labels to retry — deliberate, see [ADR-0007](./docs/adr/0007-failure-contract-status-blocked-plus-comment-no-auto-retry.md).
- **Failed runs leave residue.** Partial branches/draft PRs are left in place for inspection; the blocked-comment links what exists.
- **Prompt injection is a real surface.** Bounded, not eliminated — see [ADR-0009](./docs/adr/0009-guardrails-for-untrusted-issue-content.md).
- **Copilot CLI is out of scope.** Its auth model doesn't fit headless per-run execution; GitHub's hosted coding agent is the native route.

## Open questions

- Claude auth: the code supports `anthropic-api-key` only; whether a subscription token (`CLAUDE_CODE_OAUTH_TOKEN`) is viable/permissible for this CI-style use is unresolved — adding it would be a small `secrets.js`/entrypoint change.
- Per-issue runtime selection via an `agent:*` label (the message schema already supports it; the receiver doesn't resolve labels yet — [ADR-0006](./docs/adr/0006-single-agent-image-runtime-selected-per-message.md)).
- A scheduled sweep to re-dispatch WIP-refused issues when capacity frees, instead of a human re-label.
