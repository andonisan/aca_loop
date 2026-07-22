# AI Loop on Azure Container Apps — machine-gate executor for label-driven issue lifecycle

## Why

ds-connect already runs a spec-first, label-driven issue lifecycle (`status:*` labels, CONTRIBUTING.md) with two explicit **machine gates**: `/ds:propose` may only run on `status:ready-for-proposal` and `/ds:implement` only on `status:ready-for-implementation`. Today a human must notice the gate label and launch the agent from their laptop — the process is automatable by design but not automated. Meanwhile the ds3d `github-runner` stack (webhook receiver → Storage Queue → KEDA-scaled ACA Job) already proves the event-driven, scale-to-zero container pattern on Azure. This change connects the two: when an issue reaches a machine-gate label, a cloud agent picks it up and runs the repo's own command headlessly, closing the loop between human review stages.

## What Changes

- New Azure Storage Queue (`ai-jobs`) alongside the existing runner queue.
- Webhook handling for `issues` events: filter to `action == "labeled"` where the label is one of the two machine-gate labels (`status:ready-for-proposal`, `status:ready-for-implementation`); drop events sent by the automation's own bot identity; enqueue `{repo, issue_number, gate_label, agent_runtime}`.
- New KEDA-scaled Azure Container Apps Job (`ai-agent`): scale 0..N on queue depth, one message per execution, same dequeue/poison-message/delete-on-success semantics as the existing runner job.
- New agent container image bundling git, `gh`, Claude Code, and opencode (both read the same `.claude/commands` / `.opencode/commands` files in the target repo, so the runtime is a per-message config knob, not an architectural fork). Copilot CLI is explicitly out of scope for v1 (headless auth tied to a user subscription; the hosted Copilot coding agent is the native alternative).
- Entrypoint contract per execution: dequeue → re-check the issue still carries the gate label (exit 0 if it moved — mirrors the runner's "empty queue? exit 0") → mint a short-lived GitHub App installation token → clone the repo → run `/ds:propose` or `/ds:implement` headlessly → on failure, flip the issue to `status:blocked` and comment the failure context, preserving the "label is the sole source of truth" invariant.
- GitHub App with Issues R/W, Contents R/W, Pull Requests R/W scoped to target repos; private key and agent API keys (Anthropic, opencode providers) live in Key Vault, read via managed identity.

## Capabilities

### New Capabilities
- `issue-gate-webhook`: receiving GitHub `issues` webhooks, filtering to machine-gate label events, deduplicating/bot-filtering, and enqueueing agent work items.
- `ai-agent-job`: the queue-driven, scale-to-zero ACA Job — scaling rules, execution lifecycle, message semantics (visibility timeout, poison messages, delete-on-success), timeout policy.
- `agent-execution`: what happens inside one agent run — gate re-validation, token minting, clone, headless command invocation, runtime selection (Claude Code vs opencode), failure handling to `status:blocked`.
- `agent-infrastructure`: Terraform-managed Azure resources — queue, ACA job, user-assigned identity, role assignments, Key Vault secrets, container image.

### Modified Capabilities
<!-- none — this repo has no existing specs; the ds3d/github-runner stack is a reference pattern, not modified by this change (whether to extend its receiver or deploy a sibling is a design decision) -->

## Impact

- **New infra**: one Storage Queue, one ACA Job, one user-assigned identity + role assignments, Key Vault secrets (GitHub App key, Anthropic/opencode API keys), one container image in ACR. Reuses (or mirrors) the existing ACA environment, storage account, and Key Vault from the github-runner stack.
- **GitHub side**: a GitHub App (new, or the existing runner App with expanded permissions — design decision leaning **new App** to keep the CI App's narrow Administration scope) subscribed to `Issues` events and installed on target repos (ds-connect first).
- **Target repo contract**: relies on ds-connect's `status:*` single-label invariant and the `/ds:propose` / `/ds:implement` commands being headless-safe — the latter is an unvalidated assumption and the top de-risking spike.
- **Cost**: Consumption-billed executions that run far longer than CI jobs (agent sessions can be 30–60+ min); `replica_timeout_seconds` must be sized for agent work, plus LLM API spend per run (needs per-run caps, e.g. `--max-turns`).
- **Security surface**: issue bodies are untrusted input driving an agent with repo write access. The gate is that only users with triage permission can apply labels; the App token is scoped per-repo; agents write branches/PRs only, never default branches (branch protection).
