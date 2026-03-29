using 'main.bicep'

param location = 'eastus2'
param cosmosAccountName = 'cosmos-acount-name'
param databaseName = 'cosmos-db-name'
param principalId = 'Entra ID of the user or service principal to assign permissions to'
param tags = {
  owner: 'user or team name'
}
param deployControlPlaneRbac = false
