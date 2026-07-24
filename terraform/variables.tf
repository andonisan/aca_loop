variable "subscription_id" {
  description = "Azure subscription ID to deploy into."
  type        = string
}

variable "location" {
  description = "Azure region for all resources."
  type        = string
  default     = "westeurope"
}

variable "resource_group_name" {
  description = "Name of the resource group to create."
  type        = string
  default     = "rg-ai-loop"
}

variable "name_prefix" {
  description = "Prefix used for naming all resources. Must be short/lowercase-friendly: it's reused (sanitized) in the globally-unique storage account and Key Vault names."
  type        = string
  default     = "ai-loop"
}

variable "agent_max_turns" {
  description = "Per-run cap on agent turns, passed to the CLI (claude --max-turns). Bounds LLM spend on a runaway session."
  type        = number
  default     = 80
}

variable "max_concurrent_agents" {
  description = "Upper bound on simultaneous agent job executions (KEDA max_executions). ds-connect's /ds:implement additionally enforces its own WIP limit of 4."
  type        = number
  default     = 4
}

variable "replica_timeout_seconds" {
  description = "Hard timeout for a single agent execution. Agent sessions run far longer than CI jobs; the entrypoint arms its own watchdog below this so the failure contract (item -> its mapped on_failure_state + comment) runs before the platform kills the replica."
  type        = number
  default     = 3600
}

variable "queue_polling_interval_seconds" {
  description = "How often KEDA polls the storage queue depth to decide whether to scale the agent job."
  type        = number
  default     = 30
}

# --- GitHub (always on) ---

variable "github_app_id" {
  description = "GitHub App ID (numeric) of the AI-loop App (separate from any CI runner App). Created manually; see README prerequisites."
  type        = string
}

variable "github_app_installation_id" {
  description = "Installation ID of the AI-loop GitHub App on the target org/repos."
  type        = string
}

variable "github_bot_login" {
  description = "GitHub login of this automation's own App bot (e.g. \"ai-loop-agent[bot]\"). Label events from this sender are dropped so the loop can't trigger itself."
  type        = string
}

# --- GitLab (optional) ---

variable "enable_gitlab" {
  description = "Provision the GitLab vendor's Key Vault secrets (webhook secret) and wire its env vars onto the receiver/agent. The GitLab access token itself is never a Terraform variable -- pushed to Key Vault out-of-band, like the GitHub App private key."
  type        = bool
  default     = false
}

variable "gitlab_base_url" {
  description = "GitLab instance base URL. Only used when enable_gitlab is true."
  type        = string
  default     = "https://gitlab.com"
}

variable "gitlab_bot_username" {
  description = "GitLab username of this automation's own bot account. Label events from this sender are dropped so the loop can't trigger itself. Required when enable_gitlab is true."
  type        = string
  default     = ""
}

# --- Azure DevOps (optional) ---

variable "enable_azure_devops" {
  description = "Provision the Azure DevOps vendor's Key Vault secrets (service hook basic-auth password) and wire its env vars onto the receiver/agent. The PAT itself is never a Terraform variable -- pushed to Key Vault out-of-band, like the GitHub App private key."
  type        = bool
  default     = false
}

variable "azure_devops_organization" {
  description = "Azure DevOps organization name, e.g. \"contoso\" for https://dev.azure.com/contoso. Required when enable_azure_devops is true."
  type        = string
  default     = ""
}

variable "azure_devops_bot_identity" {
  description = "Display name or unique name (email) of this automation's own Azure DevOps identity, as it appears in a work item's revisedBy. Events from this identity are dropped so the loop can't trigger itself. Required when enable_azure_devops is true."
  type        = string
  default     = ""
}

variable "azure_devops_webhook_username" {
  description = "Basic-auth username to configure on the Azure DevOps Service Hooks subscription. Only used when enable_azure_devops is true."
  type        = string
  default     = "aca-hook"
}

# --- Container images / registry ---

variable "container_registry_id" {
  description = "Resource ID of the Azure Container Registry hosting the receiver and agent images. Used to scope the AcrPull role assignments."
  type        = string
}

variable "container_registry_login_server" {
  description = "Login server hostname of the same registry, e.g. myregistry.azurecr.io."
  type        = string
}

variable "webhook_receiver_image" {
  description = "Fully-qualified image reference for the Receiver (src/root/Orchestrator.Receiver/Dockerfile, built with context src/), e.g. myregistry.azurecr.io/ai-loop-receiver:latest"
  type        = string
}

variable "agent_image" {
  description = "Fully-qualified image reference for the Agent (src/root/Orchestrator.Agent/Dockerfile, built with context src/), e.g. myregistry.azurecr.io/ai-loop-agent:latest"
  type        = string
}

variable "tags" {
  description = "Tags applied to all resources."
  type        = map(string)
  default = {
    project = "ai-loop"
  }
}
