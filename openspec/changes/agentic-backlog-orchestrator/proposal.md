# Agentic Backlog Orchestrator — multi-vendor, configurable state-to-action engine

## Why

The v1 in this repo (`ai-loop-aca-job`) proved the pattern — a gate label on a GitHub issue dispatches a scale-to-zero ACA Job that runs the repo's own agent command — but everything that makes it work is hardcoded to one vendor (GitHub) and one client (ds-connect: two fixed labels, two fixed `/ds:*` commands). Teams live on different backlog vendors (Azure DevOps, GitHub, GitLab) with equivalent-but-differently-shaped lifecycles, so the automation must become a product: a backlog abstraction plus a configurable "state → AI action" engine, reusing the already-proven AI-pod dispatch on Azure Container Apps. See [`docs/products/orquestador-agentico-backlog.md`](../../../docs/products/orquestador-agentico-backlog.md) for the full product brief.

## What Changes

- Introduce a **backlog abstraction**: a canonical work-item + state model over Azure DevOps, GitHub, and GitLab, with one vendor adapter each that (a) normalizes the vendor's native state-change notification (issue `labeled` webhook / work-item-updated service hook / GitLab issue-label webhook) into a canonical event, and (b) writes state transitions back to the vendor in its native mechanism (labels / work item state), including the failure → "blocked" contract.
- Introduce a **configurable state→action mapping**: per client/repo configuration declaring which canonical state dispatches which agent instruction (command), with which runtime — replacing v1's two hardcoded gate labels and fixed `/ds:propose` / `/ds:implement` commands. The gate re-validation invariant ("is the item still in the gate state?") is preserved but evaluated against the configured mapping.
- Expose **agent runtime selection as product configuration**: the agent image already bundles Claude Code, opencode, and `gh`; the runtime becomes a per-mapping/per-dispatch config knob rather than an internal deployment detail.
- Generalize the **AI-pod dispatch**: same webhook-receiver → Storage Queue → KEDA-scaled ACA Job pattern as v1, but the queue message and the receiver become vendor-agnostic (`{vendor, repo/project, item_id, canonical_state, action, runtime}`) and multi-repo/multi-client capable.
- **BREAKING** (internal): the v1 queue message schema `{repo, issue_number, gate_label, agent_runtime}` is replaced by the vendor-agnostic schema; receiver and agent entrypoint change together.

## Capabilities

### New Capabilities
- `backlog-abstraction`: the canonical work-item/state model and the per-vendor adapters (GitHub, Azure DevOps, GitLab) — event normalization inbound, state write-back outbound (including the failure→blocked equivalent per vendor).
- `state-action-config`: the configurable mapping "native state → canonical state → agent action + runtime" per client/repo — schema, validation, and how the receiver and the agent's gate re-validation consume it.
- `ai-pod-dispatch`: the vendor-agnostic dispatch pipeline — receiver filtering against configured mappings, canonical queue message, KEDA-scaled ACA Job ("AI pod") execution semantics generalized from v1.
- `agent-runtime-selection`: selecting the agent runtime (Claude Code | opencode | gh-based) per dispatch from configuration, on the single multi-runtime image.

### Modified Capabilities
<!-- none — openspec/specs/ is empty; the v1 change (ai-loop-aca-job) was never archived, so its capabilities are superseded by the new ones above rather than modified -->

## Impact

- **Code**: `webhook-receiver/` becomes the vendor-adapter host (GitHub adapter extracted from current logic; Azure DevOps and GitLab adapters new); `agent/` entrypoint reads action/runtime from the canonical message and the mapping config instead of hardcoded gate-label→command pairs.
- **Infra (`terraform/`)**: mostly reused (queue, ACA environment, receiver app, agent job, identities, Key Vault); new secrets per vendor (Azure DevOps PAT/service-connection credentials, GitLab token) alongside the GitHub App key.
- **Config surface**: a new mapping-configuration artifact (per repo/client) becomes the product's core contract; where it lives (in the target repo vs. centrally) is a design decision.
- **Target repo contract**: unchanged in spirit — repos still own their agent commands (`.claude/commands` / `.opencode/commands`); the single-state invariant (exactly one lifecycle state per item) must hold per vendor for gate re-validation to stay the lock.
- **Security surface**: grows with each vendor — three credential types, three webhook signature schemes; the "who can change the state is the authorization gate" principle must map to each vendor's permission model (GitHub triage, Azure DevOps work-item permissions, GitLab reporter+).
