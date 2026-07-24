# ADR-0003: A dedicated GitHub App, separate from the CI runner App

- **Status:** Accepted
- **Date:** 2026-07-24
- **Deciders:** Andoni Santamaria
- **Tags:** security, github

## Context

The org already has a GitHub App for the self-hosted CI runner stack, holding Administration read/write (for JIT runner configs) and nothing else. This stack needs a different privilege set: Issues R/W, Contents R/W, Pull Requests R/W, plus an `issues` webhook subscription. One App could carry both sets.

## Decision

We will create a dedicated GitHub App for the AI loop rather than extending the runner App. Two narrow Apps beat one broad one: each holds only the permissions its stack needs, can be revoked or rotated independently, and produces separately attributable audit logs. The App is installed only on target repos (ds-connect first).

## Consequences

- **Positive:** independent blast radius, revocation, and audit; the runner App never gains repo-content write access; this App never gains Administration.
- **Negative:** one more App to create manually, one more private key to custody (in Key Vault).
- **Neutral:** the manual App-creation steps in `README.md` are the deliberate mirror of the runner stack's setup.

## Alternatives considered

- **Extend the existing runner App** — rejected: fewer moving parts, but couples unrelated privilege sets; compromising one stack would expose both capabilities.
- **A personal access token** — rejected: not scopeable per-repo per-run, tied to a human account, and long-lived by nature (see [ADR-0008](./0008-managed-identity-jit-tokens-secrets-only-in-key-vault.md)).

## References

- Source decision: D3 in `openspec/changes/ai-loop-aca-job/design.md`
- Related: [ADR-0008](./0008-managed-identity-jit-tokens-secrets-only-in-key-vault.md)
