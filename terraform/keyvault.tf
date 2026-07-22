resource "azurerm_key_vault" "this" {
  # Key Vault names must be 3-24 chars, alphanumeric/hyphens, start with a letter.
  name                       = substr("${var.name_prefix}-kv-${random_string.suffix.result}", 0, 24)
  location                   = azurerm_resource_group.this.location
  resource_group_name        = azurerm_resource_group.this.name
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  rbac_authorization_enabled = true
  purge_protection_enabled   = false
  soft_delete_retention_days = 7
  tags                       = var.tags
}

# GitHub requires the webhook secret up front when configuring the App, so
# Terraform generates it and surfaces it via an output -- copy it into the
# GitHub App's webhook settings during the manual setup step.
resource "random_password" "github_webhook_secret" {
  length  = 48
  special = false
}

# Both the webhook receiver and the agent job fetch secrets from this vault
# themselves at runtime via their managed identity -- nothing is wired
# through Container Apps' native secret bindings, so there's no apply-order
# dependency on the secrets pushed out-of-band and never referenced from
# Terraform state:
#   az keyvault secret set --name github-app-private-key --file key.pem ...
#   az keyvault secret set --name anthropic-api-key --value ...
#   az keyvault secret set --name opencode-provider-keys --value '<json>'  (optional)
resource "azurerm_key_vault_secret" "github_webhook_secret" {
  name         = "github-webhook-secret"
  value        = random_password.github_webhook_secret.result
  key_vault_id = azurerm_key_vault.this.id

  depends_on = [time_sleep.kv_rbac_propagation]
}

resource "azurerm_key_vault_secret" "github_app_id" {
  name         = "github-app-id"
  value        = var.github_app_id
  key_vault_id = azurerm_key_vault.this.id

  depends_on = [time_sleep.kv_rbac_propagation]
}

# RBAC-authorized vaults grant the Terraform caller no data-plane access by
# default; this lets `terraform apply` itself write the secrets above.
resource "azurerm_role_assignment" "terraform_deployer_kv_admin" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = data.azurerm_client_config.current.object_id
}

# Azure RBAC role assignments are eventually consistent; without this,
# the secret writes above routinely 403 on a fresh vault.
resource "time_sleep" "kv_rbac_propagation" {
  depends_on      = [azurerm_role_assignment.terraform_deployer_kv_admin]
  create_duration = "30s"
}
