# ADR-0009: Guardrails for untrusted issue content driving a write-capable agent

- **Status:** Accepted
- **Date:** 2026-07-24
- **Deciders:** Andoni Santamaria
- **Tags:** security, agent

## Context

The agent reads issue bodies and PR review comments — arbitrary text, potentially adversarial — and acts on them while holding a repo-scoped write token. Prompt injection is therefore a structural risk, not an edge case: a malicious issue could try to steer the agent into destructive pushes, data exfiltration, or runaway spend. It cannot be eliminated while the product exists; it must be bounded.

## Decision

We will bound the blast radius with layered, mostly-external controls rather than trusting the model to resist injection:

1. **Authorization gate**: only GitHub triage permission can apply labels — untrusted users can write issue text but cannot dispatch an agent against it.
2. **Token scope**: per-run installation token, single repo, ≤1 h ([ADR-0008](./0008-managed-identity-jit-tokens-secrets-only-in-key-vault.md)).
3. **Branch protection** on target repos: the agent can only push branches and open PRs; the default branch is unreachable.
4. **Spend caps**: `AGENT_MAX_TURNS` (default 80) and the replica timeout (default 3600 s) bound any single run.
5. **Tool policy**: the Claude Code invocation passes an explicit `--allowed-tools` list (`AGENT_ALLOWED_TOOLS`, tunable without rebuild) instead of `--dangerously-skip-permissions`.

## Consequences

- **Positive:** a fully-hijacked run is limited to: noise on branches/PRs of one repo, bounded LLM spend, and comments — all visible and reversible; the controls are enforceable infrastructure, not prompt engineering.
- **Negative:** the allowed-tools list can block legitimate command behaviour and needs tuning per target repo's commands; residual risk remains within the granted capabilities (e.g. misleading PR content a reviewer might merge).
- **Neutral:** human review gates (spec review, code review) stay the final backstop — this stack never merges anything.

## Alternatives considered

- **Trusting model-side injection resistance alone** — rejected: unenforceable and unauditable; capability bounding works regardless of model behaviour.
- **`--dangerously-skip-permissions` for frictionless runs** — rejected: abandons the cheapest structural control on what a hijacked session can execute.
- **Sanitizing/filtering issue bodies before the agent sees them** — rejected: an arms race with no sound end state; the issue text *is* the work order and must reach the agent.

## References

- Source decision: D9 in `openspec/changes/ai-loop-aca-job/design.md`
- Related: [ADR-0003](./0003-dedicated-github-app-separate-from-ci-runner-app.md), [ADR-0008](./0008-managed-identity-jit-tokens-secrets-only-in-key-vault.md)
