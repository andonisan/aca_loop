terraform {
  required_version = ">= 1.6.0"

  required_providers {
    azurerm = {
      source = "hashicorp/azurerm"
      # >= 4.73 for azurerm_container_app_job's scale-rule `identity_id`
      # (managed-identity auth for the KEDA azure-queue scaler).
      version = "~> 4.73"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
    time = {
      source  = "hashicorp/time"
      version = "~> 0.12"
    }
  }
}

provider "azurerm" {
  features {}
  subscription_id = var.subscription_id
}
