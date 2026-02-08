// ──────────────────────────────────────────────────────────────
// AdventureWorks Cosmos DB Infrastructure — Subscription-scoped
// ──────────────────────────────────────────────────────────────
targetScope = 'subscription'

// ── Parameters ────────────────────────────────────────────────

@description('Azure region for all resources.')
param location string

@description('Name of the resource group to create.')
param resourceGroupName string

@description('Name of the Cosmos DB account.')
param cosmosAccountName string

@description('Name of the Cosmos DB SQL database.')
param databaseName string = 'adventureworks'

@description('Entra ID principal ID for RBAC assignments (data plane + control plane).')
param principalId string

@description('Tags applied to every resource.')
param tags object = {}

// ── Resource Group ────────────────────────────────────────────

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

// ── Cosmos DB (resource-group scoped module) ──────────────────

module cosmos 'modules/cosmos.bicep' = {
  scope: rg
  params: {
    location: location
    cosmosAccountName: cosmosAccountName
    databaseName: databaseName
    principalId: principalId
    tags: tags
  }
}

// ── Outputs ───────────────────────────────────────────────────

@description('Cosmos DB account name.')
output cosmosAccountName string = cosmos.outputs.accountName

@description('Cosmos DB account endpoint.')
output cosmosEndpoint string = cosmos.outputs.endpoint

@description('Resource group name.')
output resourceGroupName string = rg.name
