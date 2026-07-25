# AGENTS.md — aca_loop

Canonical, tool-neutral instructions for AI coding agents (Claude Code, opencode, GitHub Copilot, …) working in this repository. `CLAUDE.md` just points here. Keep this file a **router**: it says where things are and how we work — it does not restate the docs it links to.

## What this repo is

The infrastructure that closes ds-connect's issue-lifecycle loop with cloud agents: when an issue reaches one of two machine-gate labels, a webhook receiver enqueues a work item and a scale-to-zero Azure Container Apps Job runs the target repo's own `/ds:propose` or `/ds:implement` command headlessly. Terraform + two small Node services. See [`docs/context.md`](./docs/context.md) for the why and [`ARCHITECTURE.md`](./ARCHITECTURE.md) for the technical map.

Note the recursion: this repo *builds* the agent dispatcher, and you may *be* an agent working on it. The target-repo contract you're implementing is described in [`docs/context-map.md`](./docs/context-map.md); don't confuse this repo's own workflow with ds-connect's `/ds:*` workflow that the stack dispatches.

## Where things live

| You need… | Look in |
| --- | --- |
| Why the stack exists (domain, actors) | [`docs/context.md`](./docs/context.md) |
| Technical architecture + invariants | [`ARCHITECTURE.md`](./ARCHITECTURE.md) |
| Neighbours and the target-repo contract | [`docs/context-map.md`](./docs/context-map.md) |
| Design rationale, risks, open questions | [`DESIGN.md`](./DESIGN.md) |
| Decisions (and how to record new ones) | [`docs/adr/`](./docs/adr/) |
| **Behavioural source of truth** | [`openspec/`](./openspec/) (changes + specs) |
| Azure stack | [`terraform/`](./terraform/) |
| Receiver / agent code | [`webhook-receiver/src/`](./webhook-receiver/src/) · [`agent/src/`](./agent/src/) |
| Deployment quick-start + manual GitHub App steps | [`README.md`](./README.md) |
| New-contributor path | [`ONBOARDING.md`](./ONBOARDING.md) |

## The workflow

Work goes through OpenSpec changes (`openspec/changes/<name>/` with proposal → design → specs → tasks, then implementation against the tasks). Use an existing change as the template. Architectural decisions get an ADR (copy [`docs/adr/template.md`](./docs/adr/template.md)); superseding one means a new ADR plus a status flip on the old, never an edit to its content.

## House rules for agents

- **Respect the load-bearing invariants** in [`ARCHITECTURE.md`](./ARCHITECTURE.md#load-bearing-invariants). Changing one requires a new ADR in the same change.
- **Never touch secret handling casually.** No secrets in Terraform variables, state, logs, or code; Key Vault + out-of-band pushes only ([ADR-0008](./docs/adr/0008-managed-identity-jit-tokens-secrets-only-in-key-vault.md)).
- **Treat issue/PR content as untrusted input** in anything you build here — this stack's whole security posture assumes it ([ADR-0009](./docs/adr/0009-guardrails-for-untrusted-issue-content.md)).
- **Keep docs as maps.** Each fact has one owning document; link, don't restate. Behaviour belongs in `openspec/`, rationale in ADRs, orientation here and in `ARCHITECTURE.md`.
- **Don't run `terraform apply` or push images** unless explicitly asked — plan-level verification only by default.
- **Confirm shared-state actions** (creating issues/labels/PRs, editing GitHub state) before executing.
