// ---------------------------------------------------------------------------
// AdventureWorksLT — Azure Cosmos DB NoSQL Infrastructure
// Provisions: Account, Database, 2 Containers, RBAC Role Assignments
// Auth model: Entra ID (DefaultAzureCredential) — no access keys
// ---------------------------------------------------------------------------

@description('Azure region for the Cosmos DB account.')
param location string

@description('Name of the Cosmos DB account.')
param cosmosAccountName string

@description('Name of the Cosmos DB database.')
param databaseName string

@description('Object (principal) ID of the Entra ID identity to grant RBAC roles.')
param principalId string

@description('Resource tags.')
param tags object = {}

@description('Deploy ARM-level RBAC role assignments. Requires Microsoft.Authorization/roleAssignments/write. Set to false if the deploying principal lacks this permission.')
param deployControlPlaneRbac bool = true

// ---------------------------------------------------------------------------
// Cosmos DB Account
// ---------------------------------------------------------------------------
resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: cosmosAccountName
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    enableAutomaticFailover: false
    enableMultipleWriteLocations: false
    disableLocalAuth: true                 // Enforce Entra-ID-only auth
    capabilities: [
      {
        name: 'EnableServerless'           // Serverless for dev; switch to autoscale for prod
      }
    ]
    backupPolicy: {
      type: 'Continuous'
      continuousModeProperties: {
        tier: 'Continuous7Days'
      }
    }
  }
}

// ---------------------------------------------------------------------------
// Database
// ---------------------------------------------------------------------------
resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' = {
  parent: cosmosAccount
  name: databaseName
  properties: {
    resource: {
      id: databaseName
    }
  }
}

// ---------------------------------------------------------------------------
// Container: customer-orders
// Partition key: /customerId
// TTL: enabled (per-document) — customer docs use ttl:-1, salesOrder docs use ttl:63072000
// ---------------------------------------------------------------------------
resource customerOrdersContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: database
  name: 'customer-orders'
  properties: {
    resource: {
      id: 'customer-orders'
      partitionKey: {
        paths: ['/customerId']
        kind: 'Hash'
        version: 2
      }
      defaultTtl: -1                       // TTL system enabled; docs without ttl property never expire
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          { path: '/type/?' }
          { path: '/customerId/?' }
          { path: '/orderDate/?' }
          { path: '/status/?' }
          { path: '/salesOrderNumber/?' }
          { path: '/firstName/?' }
          { path: '/lastName/?' }
          { path: '/emailAddress/?' }
          { path: '/companyName/?' }
        ]
        excludedPaths: [
          { path: '/*' }
          { path: '/"_etag"/?' }
        ]
        compositeIndexes: [
          [
            { path: '/type', order: 'ascending' }
            { path: '/orderDate', order: 'descending' }
          ]
          [
            { path: '/type', order: 'ascending' }
            { path: '/lastName', order: 'ascending' }
          ]
        ]
      }
    }
  }
}

// ---------------------------------------------------------------------------
// Container: product-catalog
// Partition key: /partitionKey (synthetic per-document key)
// TTL: disabled (-1 at container level means TTL system on but docs without ttl never expire;
//      we set no defaultTtl so TTL system is off entirely — catalog data is permanent)
// ---------------------------------------------------------------------------
resource productCatalogContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: database
  name: 'product-catalog'
  properties: {
    resource: {
      id: 'product-catalog'
      partitionKey: {
        paths: ['/partitionKey']
        kind: 'Hash'
        version: 2
      }
      // No defaultTtl — TTL system disabled; catalog data never expires
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          { path: '/type/?' }
          { path: '/name/?' }
          { path: '/productNumber/?' }
          { path: '/productCategoryId/?' }
          { path: '/productModelId/?' }
          { path: '/listPrice/?' }
          { path: '/color/?' }
          { path: '/parentProductCategoryId/?' }
        ]
        excludedPaths: [
          { path: '/*' }
          { path: '/"_etag"/?' }
        ]
        compositeIndexes: [
          [
            { path: '/type', order: 'ascending' }
            { path: '/name', order: 'ascending' }
          ]
          [
            { path: '/type', order: 'ascending' }
            { path: '/listPrice', order: 'ascending' }
          ]
        ]
      }
    }
  }
}

// ---------------------------------------------------------------------------
// RBAC Role Assignments — Entra ID
// ---------------------------------------------------------------------------

// Built-in role: Cosmos DB Operator (control-plane) 
// Allows managing accounts, databases, containers — but NOT data read/write
var cosmosDbOperatorRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '230c5e1-5e2d-4601-986a-6d7b9a736585'   // Cosmos DB Operator
)

resource operatorRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployControlPlaneRbac) {
  name: guid(cosmosAccount.id, principalId, cosmosDbOperatorRoleDefinitionId)
  scope: cosmosAccount
  properties: {
    principalId: principalId
    roleDefinitionId: cosmosDbOperatorRoleDefinitionId
    principalType: 'User'
  }
}

// Built-in Cosmos DB data-plane role: Cosmos DB Built-in Data Contributor
// Allows full read/write access to data within containers
// This is a Cosmos DB-native RBAC role (not an ARM role), assigned via sqlRoleAssignment
var dataContributorRoleDefinitionId = '00000000-0000-0000-0000-000000000002' // Built-in Data Contributor

resource dataContributorRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = {
  parent: cosmosAccount
  name: guid(cosmosAccount.id, principalId, dataContributorRoleDefinitionId)
  properties: {
    principalId: principalId
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/${dataContributorRoleDefinitionId}'
    scope: cosmosAccount.id
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------
output cosmosAccountName string = cosmosAccount.name
output cosmosAccountEndpoint string = cosmosAccount.properties.documentEndpoint
output databaseName string = database.name
output customerOrdersContainerName string = customerOrdersContainer.name
output productCatalogContainerName string = productCatalogContainer.name
