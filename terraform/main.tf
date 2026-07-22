data "azurerm_client_config" "current" {}

resource "azurerm_resource_group" "this" {
  name     = var.resource_group_name
  location = var.location
  tags     = var.tags
}

# Storage account and Key Vault names must be globally unique across Azure;
# this suffix keeps var.name_prefix human-readable while still being unique.
resource "random_string" "suffix" {
  length  = 6
  special = false
  upper   = false
}
