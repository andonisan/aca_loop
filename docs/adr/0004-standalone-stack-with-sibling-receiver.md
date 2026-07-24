# ADR-0004: A standalone stack in this repo, with its own sibling webhook receiver

- **Status:** Accepted
- **Date:** 2026-07-24
- **Deciders:** Andoni Santamaria
- **Tags:** infra, azure

## Context

The ds3d `github-runner` stack already runs an always-on webhook receiver. Extending it with an `issues` route (second HMAC secret, second queue) would avoid a second `min_replicas=1` container — the original design leaned that way. But it would couple this stack's deploys, failures, and lifecycle to the CI runner's, and require changes in that repo.

## Decision

We will host a complete standalone stack in this repo: its own resource group, storage account + queue, Key Vault, Container Apps environment, a small `issues`-only webhook receiver, and the agent job — mirroring the runner stack's Terraform patterns rather than modifying that repo. (Revised from "extend the existing receiver" at apply time.)

## Consequences

- **Positive:** zero coupling to the CI runner stack — independent blast radius, deploys, teardown, and experimentation; this repo is self-contained and readable end to end.
- **Negative:** one extra always-on receiver replica plus per-stack Storage/Key Vault/Log Analytics — a small fixed monthly cost.
- **Neutral:** revisit consolidation if the idle cost ever matters; the receiver stays deliberately tiny so a merge would be cheap.

## Alternatives considered

- **Extend the github-runner receiver with an `issues` route** — rejected: saves one small container at the price of cross-repo coupling and a two-personality receiver.
- **Azure Functions for the receiver** — rejected: another compute model in the mix for no capability gain; ACA keeps the whole stack on one platform with one identity story.

## References

- Source decision: D4 (as revised) in `openspec/changes/ai-loop-aca-job/design.md`
- Related: [ADR-0001](./0001-webhook-queue-aca-job-over-github-actions-workflows.md)
