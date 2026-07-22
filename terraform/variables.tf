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

variable "gate_labels" {
  description = "Comma-separated issue labels that trigger an agent run. These are ds-connect's two machine gates; the receiver enqueues nothing for any other label."
  type        = string
  default     = "status:ready-for-proposal,status:ready-for-implementation"
}

variable "default_agent_runtime" {
  description = "Agent CLI used when a work item doesn't specify one: \"claude\" or \"opencode\"."
  type        = string
  default     = "claude"

  validation {
    condition     = contains(["claude", "opencode"], var.default_agent_runtime)
    error_message = "default_agent_runtime must be \"claude\" or \"opencode\"."
  }
}

variable "agent_bot_login" {
  description = "GitHub login of this automation's own App bot (e.g. \"ai-loop-agent[bot]\"). Label events from this sender are dropped so the loop can't trigger itself."
  type        = string
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
  description = "Hard timeout for a single agent execution. Agent sessions run far longer than CI jobs; the entrypoint arms its own watchdog below this so the failure contract (issue -> status:blocked + comment) runs before the platform kills the replica."
  type        = number
  default     = 3600
}

variable "queue_polling_interval_seconds" {
  description = "How often KEDA polls the storage queue depth to decide whether to scale the agent job."
  type        = number
  default     = 30
}

variable "github_app_id" {
  description = "GitHub App ID (numeric) of the AI-loop App (separate from any CI runner App). Created manually; see README prerequisites."
  type        = string
}

variable "github_app_installation_id" {
  description = "Installation ID of the AI-loop GitHub App on the target org/repos."
  type        = string
}

variable "container_registry_id" {
  description = "Resource ID of the Azure Container Registry hosting the webhook-receiver and agent images. Used to scope the AcrPull role assignments."
  type        = string
}

variable "container_registry_login_server" {
  description = "Login server hostname of the same registry, e.g. myregistry.azurecr.io."
  type        = string
}

variable "webhook_receiver_image" {
  description = "Fully-qualified image reference for the issue-gate webhook receiver, e.g. myregistry.azurecr.io/ai-loop-webhook:latest"
  type        = string
}

variable "agent_image" {
  description = "Fully-qualified image reference for the agent container (this repo's agent/Dockerfile), e.g. myregistry.azurecr.io/ai-loop-agent:latest"
  type        = string
}

variable "tags" {
  description = "Tags applied to all resources."
  type        = map(string)
  default = {
    project = "ai-loop"
  }
}
