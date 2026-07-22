# Design — AI Loop ACA Job

## Context

Two existing systems anchor this design:

1. **ds3d/github-runner** (`~/projects/ds3d/github-runner/terraform`): webhook receiver (always-on Container App) verifies HMAC, filters `workflow_job: queued`, enqueues to a Storage Queue; a KEDA-scaled ACA Job drains it one message per execution with scale-to-zero, managed-identity auth end to end, JIT-minted credentials per execution, and poison-message guards. This is the proven event→queue→ephemeral-container machine.
2. **ds-connect's issue lifecycle** (CONTRIBUTING.md): nine `status:*` labels form a state machine with a single-label invariant. Two transitions are machine gates (`ready-for-proposal` → `/ds:propose`, `ready-for-implementation` → `/ds:implement`, WIP limit 4 enforced by the command itself). Commands are runtime-portable: `.opencode/commands` symlinks `.claude/commands`; `AGENTS.md` is the tool-neutral entry.

The loop is closed by humans moving labels: approving a spec advances the label to the next gate; requesting changes moves it back to a gate label, which re-triggers the agent with the reviewer's PR comments now in context.

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

## Goals / Non-Goals

**Goals:**
- Issues reaching a machine-gate label get worked by a cloud agent with no human launch step.
- Scale to zero; pay per agent run.
- Agent runs the target repo's own commands — no agent logic lives in this infra beyond the entrypoint contract.
- Runtime (Claude Code vs opencode) selectable per run for cost/quality comparison.
- Failure never leaves the board lying: a dead run flips the issue to `status:blocked` with a comment.

**Non-Goals:**
- Copilot CLI support (headless auth tied to user subscription; hosted Copilot coding agent is the native alternative).
- Automating the human stages (`/ds:grill`, spec review, code review, `/ds:sync`).
- A generic "AI on any label" platform — v1 targets exactly the two machine gates.
- Autonomous iterate-until-CI-green loops (agent sees check results) — future work.

## Decisions

### D1: ACA Job via webhook+queue, not GitHub Actions workflows
`issues`-event workflows must exist per-repo, run on the CI runner image (coupling agent toolchain into it), and get Actions-shaped timeouts. The webhook→queue→job path is org-wide via one App install, keeps the agent image separate, and reuses the proven runner pattern. *Alternative rejected*: `on: issues: types: [labeled]` workflow on the existing self-hosted runners — cheaper to build, kept as a possible throwaway spike, but not the target.

### D2: Trigger filter = exactly the two machine-gate labels
`action == "labeled" && label ∈ {status:ready-for-proposal, status:ready-for-implementation}`. Because the agent's own label writes (`proposal-review`, `in-progress`, `code-review`, `blocked`) are never gate labels, the automation cannot re-trigger itself. Belt-and-braces: also drop deliveries where `sender` is this automation's bot. Duplicate deliveries are neutralized by gate re-validation at execution time (D5), not receiver-side state.

### D3: Separate GitHub App from the CI runner App
The runner App holds Administration R/W (JIT runner configs) and nothing else; this App needs Issues R/W + Contents R/W + Pull Requests R/W. Two narrow Apps beat one broad one: independent blast radius, revocation, and audit. *Alternative*: extend the existing App — fewer moving parts but couples unrelated privilege sets.

### D4: Webhook receiver — extend vs sibling: **sibling receiver, standalone stack in this repo** *(revised at apply time)*
Originally leaned "extend the existing receiver" to avoid a second always-on container. At apply the user chose repo independence: this repo (`aca_loop`) hosts a complete standalone stack — its own resource group, storage, Key Vault, ACA environment, a small `issues`-only webhook receiver, and the agent job — mirroring the github-runner terraform patterns rather than modifying that repo. Trade-off accepted: one extra always-on `min_replicas=1` receiver, in exchange for zero coupling to the CI runner stack (independent lifecycle, blast radius, and deploys). Revisit consolidation if the extra idle cost ever matters.

### D5: Execution-time gate re-validation (the lock)
After dequeue, the entrypoint re-reads the issue: if the gate label is gone (another execution took it, a human moved it, or this is a duplicate delivery), exit 0 and delete the message — the same "overscale is normal, exit clean" philosophy as the runner's empty-queue check. The single-label invariant makes the command's own label flip the effective lock; the re-check shrinks the race window to seconds without any distributed-lock machinery.

### D6: One fat agent image, runtime chosen per message
One image with git + `gh` + Claude Code + opencode; the queue message's `agent_runtime` field (default from Terraform var, overridable later by e.g. an `agent:*` label) picks the CLI. Both CLIs read the same command files, so this is pure config. *Alternative*: image-per-runtime with separate jobs — more infra for no isolation benefit; the CLIs are small next to the toolchain.

### D7: Failure contract — `status:blocked` + comment
Entrypoint traps failure/timeout: flip the issue's `status:*` label to `status:blocked`, comment the run outcome (log tail / failure reason), delete the message (no automatic retry of agent work — a failed agent run is not idempotent-retryable like a CI job; a human re-labels to retry). Poison-message guard remains as backstop for entrypoint-level crashes before the trap is armed. WIP-limit refusal from `/ds:implement` is treated the same way: the issue stays at its gate label, message deleted, comment explains; a human (or later, a scheduled sweep) re-labels when capacity frees.

### D8: Secrets and identity
Managed identity end to end (queue scaler, queue consume, Key Vault read, ACR pull), mirroring the runner job. Key Vault holds: this App's private key, webhook secret, `ANTHROPIC_API_KEY` (or `CLAUDE_CODE_OAUTH_TOKEN`), opencode provider keys. The GitHub installation token is minted per execution, scoped to the single target repo, and expires in ≤1h — never stored.

### D9: Guardrails on the agent run
- Issue bodies are untrusted input; the authorization gate is GitHub triage permission (who can label).
- Token scoped per-repo; branch protection ensures agent writes branches/PRs only, never default branches.
- Per-run caps: `--max-turns` / equivalent, `replica_timeout_seconds` sized for agent work (start ~3600s, distinct from the CI runner's setting).
- Allowed-tools policy passed to the CLI (e.g. `claude -p --allowedTools ...`) rather than `--dangerously-skip-permissions` where feasible.

## Risks / Trade-offs

- **[Headless-safety of `/ds:propose` / `/ds:implement` is unvalidated]** → Spike before any Terraform: run `claude -p "/ds:propose #<issue>"` in a fresh container-local clone; find where it prompts or stalls. This can invalidate the whole schedule cheaply.
- **[Agent runs are long; Consumption bills by execution time]** → per-run turn caps, timeout ceiling, WIP limit 4 already bounds concurrency; monitor cost per gate transition.
- **[Prompt injection via issue/PR content]** → triage-permission labeling gate, per-repo token scope, branch protection, no secrets in the clone beyond the run's own token.
- **[Failed run leaves partial branches/draft PRs]** → acceptable v1 residue; the `status:blocked` comment links whatever exists so a human can clean up or resume.
- **[Receiver becomes two-personality]** → acceptable now (D4); split if `issues` handling grows state (e.g. receiver-side dedup or scheduling).
- **[Copilot excluded]** → users wanting Copilot use GitHub's hosted coding agent via issue assignment, orthogonal to this stack.

## Migration Plan

1. Spike: headless `/ds:propose` in a local container against a scratch ds-connect issue (no Azure).
2. Create the new GitHub App (manual, like the runner App); install on ds-connect only.
3. Terraform: queue + identity + roles + Key Vault secrets + ACA Job (image can be a placeholder until step 4).
4. Build/push agent image; extend webhook receiver with the `issues` route (second HMAC secret, new queue).
5. Verify with GitHub webhook "Redeliver" → message appears in `ai-jobs` queue → execution runs → draft PR + label flip observed.
6. Edge cases deliberately: stale gate label (exit 0), kill mid-run (issue → blocked + comment), WIP-limit refusal, duplicate delivery.
7. Rollback: set the App webhook inactive (or uninstall) — infra can stay, it scales to zero.

## Open Questions

- ~~Where does this Terraform live~~ — **resolved**: this repo, standalone stack (see revised D4).
- `replica_timeout_seconds` ceiling for `/ds:implement` on a meaty issue — 1h? 2h? What's the spend comfort level?
- Claude auth: API key vs `CLAUDE_CODE_OAUTH_TOKEN` (subscription) — cost model and ToS for CI-style use.
- Does `/ds:implement`'s WIP-limit refusal exit non-zero (distinguishable from real failure) so D7 can leave the gate label in place instead of flipping to blocked?
- Should `agent_runtime` selection eventually come from an `agent:*` label on the issue, and if so does the receiver or the entrypoint resolve it?
