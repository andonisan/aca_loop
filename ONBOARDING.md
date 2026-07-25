# Onboarding

Welcome to the AI loop. This gets you from zero to a first contribution. For the one-time deployment commands and GitHub App prerequisites, follow [`README.md`](./README.md) — this guide doesn't repeat them.

## 1. Get oriented (30 min)

- **Why this exists** — [`docs/context.md`](./docs/context.md): the problem (closing ds-connect's machine-gate transitions), the actors, the domain terms (gate label, single-label invariant, work item, failure contract).
- **How it's built** — [`ARCHITECTURE.md`](./ARCHITECTURE.md): the three deployed pieces, the load-bearing invariants, where the code lives.
- **How it relates to its neighbours** — [`docs/context-map.md`](./docs/context-map.md): especially the *contract with target repos* — what this stack assumes about ds-connect is the part that bites when it changes.
- **Why it's built this way** — [`DESIGN.md`](./DESIGN.md), then the individual [`docs/adr/`](./docs/adr/) records as needed.

## 2. Get it running

Follow the **prerequisites and usage** in [`README.md`](./README.md): create the GitHub App, build/push the two images, `terraform apply`, push the out-of-band secrets, wire the webhook URL. Apply into a scratch resource group first. If a step fails or a doc misleads you, note it — onboarding friction is worth an issue.

## 3. Verify it end to end

1. **Receiver alone**: use GitHub's webhook **Redeliver** on a past `issues` labeled delivery carrying a gate label, then check the queue:
   ```bash
   az storage message peek --account-name <name> --queue-name ai-jobs --auth-mode login
   ```
2. **Live run**: label a scratch issue `status:ready-for-proposal`, watch
   ```bash
   az containerapp job execution list --name <agent_job_name> -g <rg>
   ```
   and confirm a draft PR opens and the label advances.
3. **Edge cases, deliberately** (each one exercises a load-bearing invariant):
   - re-label the issue away **before** the execution starts → must exit 0 with no LLM calls in the logs (stale gate — [ADR-0005](./docs/adr/0005-execution-time-gate-re-validation-as-the-concurrency-lock.md));
   - kill an execution mid-run → the issue must end at `status:blocked` with an explanatory comment ([ADR-0007](./docs/adr/0007-failure-contract-status-blocked-plus-comment-no-auto-retry.md));
   - redeliver the same webhook twice → the second execution must exit 0 at gate re-validation;
   - fill the WIP limit (four `status:in-progress` issues) and dispatch a fifth `/ds:implement` → the gate label must be kept, with a comment.
4. **The loop itself**: request changes on the draft PR, re-label to `status:ready-for-proposal`, and confirm the agent revises using the review comments.

## 4. Make your first contribution

Work goes through OpenSpec changes in [`openspec/changes/`](./openspec/changes/):

1. Propose: create a change with a proposal, design, specs, and tasks (the `/opsx:propose` flow, or by hand following an existing change as the template).
2. Implement against the tasks; keep `openspec/` as the behavioural source of truth.
3. If your change alters an architectural decision, write a new ADR (copy [`docs/adr/template.md`](./docs/adr/template.md)) and mark the old one superseded — don't edit history.

## 5. Know the guardrails

- The invariants in [`ARCHITECTURE.md`](./ARCHITECTURE.md#load-bearing-invariants) are not suggestions; changing one requires a new ADR.
- Never put a secret in Terraform variables or state — out-of-band `az keyvault secret set` only ([ADR-0008](./docs/adr/0008-managed-identity-jit-tokens-secrets-only-in-key-vault.md)).
- Treat issue content as untrusted input in anything you build ([ADR-0009](./docs/adr/0009-guardrails-for-untrusted-issue-content.md)).

## Help

- Working as an AI agent? [`AGENTS.md`](./AGENTS.md) is the tool-neutral entry point.
- Stuck on a decision? Check [`docs/adr/`](./docs/adr/), or record a new one from the template.
