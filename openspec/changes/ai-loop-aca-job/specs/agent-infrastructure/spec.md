# agent-infrastructure

## ADDED Requirements

### Requirement: Terraform-managed Azure resources
The AI-loop infrastructure SHALL be managed by Terraform in this repository as a standalone stack following the github-runner stack's patterns: its own resource group, storage account with the `ai-jobs` Storage Queue, Key Vault, Container Apps environment with Log Analytics, the webhook receiver Container App, the `ai-agent` Container Apps Job, dedicated user-assigned managed identities, and the role assignments those identities need (queue send for the receiver; queue consume for the entrypoint, queue read for the KEDA scaler, Key Vault secrets read, ACR pull for the job).

#### Scenario: Fresh apply
- **WHEN** `terraform apply` runs with the AI-loop configuration in an empty subscription scope
- **THEN** the full stack (resource group, storage + queue, Key Vault, environment, receiver app, agent job, identities, role assignments) exists with no dependency on the CI runner stack

### Requirement: Secrets live in Key Vault, never in Terraform state
The AI-loop App's private key, its webhook secret, and the LLM provider keys (Anthropic, opencode providers) SHALL be stored as Key Vault secrets pushed out-of-band, read at run time via managed identity. No secret value SHALL pass through a Terraform variable or appear in state, and no connection string SHALL be used anywhere.

#### Scenario: Secret rotation
- **WHEN** an operator updates a secret version in Key Vault
- **THEN** subsequent executions use the new value with no Terraform change or redeploy

### Requirement: Dedicated agent container image
A dedicated agent image SHALL bundle git, the `gh` CLI, Claude Code, and opencode, with the entrypoint implementing the `agent-execution` contract. The image SHALL be pulled from the container registry via managed identity and SHALL be separate from the CI runner image.

#### Scenario: Image build and pull
- **WHEN** the image is pushed to the registry and the job starts an execution
- **THEN** the job pulls it using the user-assigned identity (no registry password) and the entrypoint has all four tools on PATH

### Requirement: Dedicated GitHub App with least privilege
The AI loop SHALL use its own GitHub App — separate from the CI runner App — with Issues R/W, Contents R/W, and Pull Requests R/W permissions, subscribed to `Issues` events, installed only on target repositories. The manual App-creation steps SHALL be documented alongside the Terraform (mirroring the runner README's prerequisites section).

#### Scenario: App compromise containment
- **WHEN** the AI-loop App's credentials are revoked
- **THEN** CI runners continue operating unaffected (and vice versa)
