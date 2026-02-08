// ──────────────────────────────────────────────────────────────
// Cosmos DB Account, Database, Containers & RBAC
// Scope: Resource Group
// ──────────────────────────────────────────────────────────────

@description('Azure region.')
param location string

@description('Cosmos DB account name.')
param cosmosAccountName string

@description('SQL database name.')
param databaseName string

@description('Entra ID principal ID for RBAC.')
param principalId string

@description('Resource tags.')
param tags object = {}

// ── Well-known role IDs ───────────────────────────────────────

@description('Cosmos DB Built-in Data Contributor role definition ID (data plane).')
var dataContributorRoleId = '00000000-0000-0000-0000-000000000002'

@description('Cosmos DB Operator role definition GUID (control plane).')
var cosmosOperatorRoleId = '230815da-be43-4aae-9cb4-875f7bd000aa'

// ── Cosmos DB Account (Serverless, Entra-only) ────────────────

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: cosmosAccountName
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    capabilities: [
      { name: 'EnableServerless' }
    ]
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    disableLocalAuth: true
    disableKeyBasedMetadataWriteAccess: true
    minimalTlsVersion: 'Tls12'
    publicNetworkAccess: 'Enabled'
  }
}

// ── SQL Database ──────────────────────────────────────────────

resource sqlDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' = {
  parent: cosmosAccount
  name: databaseName
  properties: {
    resource: {
      id: databaseName
    }
  }
}

// ── Container: products (PK /id) ─────────────────────────────

resource productsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: sqlDatabase
  name: 'products'
  properties: {
    resource: {
      id: 'products'
      partitionKey: {
        paths: ['/id']
        kind: 'Hash'
        version: 2
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          { path: '/docType/?' }
          { path: '/name/?' }
          { path: '/productCategoryId/?' }
          { path: '/productModelId/?' }
          { path: '/productNumber/?' }
        ]
        excludedPaths: [
          { path: '/*' }
        ]
      }
    }
  }
}

// ── Container: customers (PK /customerId) ─────────────────────

resource customersContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: sqlDatabase
  name: 'customers'
  properties: {
    resource: {
      id: 'customers'
      partitionKey: {
        paths: ['/customerId']
        kind: 'Hash'
        version: 2
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          { path: '/docType/?' }
          { path: '/customerId/?' }
          { path: '/lastName/?' }
          { path: '/firstName/?' }
          { path: '/orderDate/?' }
          { path: '/status/?' }
          { path: '/salesOrderNumber/?' }
        ]
        excludedPaths: [
          { path: '/*' }
        ]
      }
    }
  }
}

// ── Data-plane RBAC: Cosmos DB Built-in Data Contributor ──────

resource dataPlaneRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = {
  parent: cosmosAccount
  name: guid(cosmosAccount.id, principalId, dataContributorRoleId)
  properties: {
    principalId: principalId
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/${dataContributorRoleId}'
    scope: cosmosAccount.id
  }
}

// ── Control-plane RBAC: Cosmos DB Operator ────────────────────

resource controlPlaneRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(cosmosAccount.id, principalId, cosmosOperatorRoleId)
  scope: cosmosAccount
  properties: {
    principalId: principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cosmosOperatorRoleId)
    principalType: 'User'
  }
}

// ── Outputs ───────────────────────────────────────────────────

@description('Cosmos DB account name.')
output accountName string = cosmosAccount.name

@description('Cosmos DB account endpoint.')
output endpoint string = cosmosAccount.properties.documentEndpoint
