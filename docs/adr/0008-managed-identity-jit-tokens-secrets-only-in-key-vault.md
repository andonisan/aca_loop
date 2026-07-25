# ADR-0008: Managed identity end to end, JIT GitHub tokens, secrets only in Key Vault

- **Status:** Accepted
- **Date:** 2026-07-24
- **Deciders:** Andoni Santamaria
- **Tags:** security, azure, github

## Context

The stack touches four credential surfaces: Azure Storage (queue), Key Vault, the container registry, and GitHub (as an App with repo write access). Connection strings and long-lived tokens are the classic leak vectors — in Terraform state, in env dumps, in logs. The github-runner stack already proved the stricter alternative on Azure.

## Decision

We will use managed identity for every intra-Azure interaction — queue send (receiver), queue consume and the KEDA scale rule itself (agent job), Key Vault reads, and ACR pulls — with least-privilege RBAC per identity (the receiver can read exactly one secret: the webhook secret). GitHub credentials are minted per execution: an installation token scoped to the single target repo, expiring in ≤1 hour, never stored. Key Vault is the only place secrets live (`github-webhook-secret`, `github-app-id`, `github-app-private-key`, `anthropic-api-key`, optional `opencode-provider-keys`); the App private key and provider keys are pushed out-of-band, never through a Terraform variable or state.

## Consequences

- **Positive:** no connection strings anywhere; a leaked agent environment yields at most a ≤1 h single-repo token; secret rotation touches one vault; Terraform state stays free of out-of-band secrets.
- **Negative:** RBAC propagation delays complicate fresh applies (mitigated with an explicit wait); out-of-band secret pushes are a manual step that can be forgotten (the containers fail loudly at startup/dequeue if so).
- **Neutral:** secrets are read at process start into memory only — rotation requires a replica restart to pick up.

## Alternatives considered

- **Storage connection strings / SAS tokens** — rejected: long-lived bearer credentials in env and state, exactly what the pattern exists to avoid.
- **A static GitHub PAT or App token in Key Vault** — rejected: unscoped-in-time credential holding repo write access; JIT minting bounds every run.
- **Container Apps native secret bindings from Key Vault** — rejected: creates an apply-order dependency on secrets that are deliberately pushed out-of-band; runtime fetch via identity keeps Terraform decoupled.

## References

- Source decision: D8 in `openspec/changes/ai-loop-aca-job/design.md`
- Implementation: `terraform/identities.tf`, `terraform/keyvault.tf`, `agent/src/github.js`
