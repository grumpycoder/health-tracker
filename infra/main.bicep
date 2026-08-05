// Fit Recovery Log — cloud sync backend (Phase 1)
// Azure SQL (free offering) + Functions (Consumption) + storage, all free-tier.
//
// NOT YET DEPLOYED. Validate before use:
//   az bicep build --file infra/main.bicep
//   az deployment group what-if -g <rg> -f infra/main.bicep -p @infra/main.parameters.json
//
// Auth is a PERSONAL Microsoft identity (not the company tenant) — see docs/sync-architecture.md.

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Short prefix for resource names (lowercase, 3-10 chars).')
@minLength(3)
@maxLength(10)
param namePrefix string = 'fitlog'

@description('Azure SQL admin login.')
param sqlAdminLogin string

@description('Azure SQL admin password.')
@secure()
param sqlAdminPassword string

@description('OIDC authority for the personal Microsoft identity that signs in.')
param authAuthority string = 'https://login.microsoftonline.com/consumers/v2.0'

@description('API app registration client id (expected JWT audience).')
param authAudience string

@description('The only user oid/sub allowed to sync (single-user lockdown). Empty = any valid token.')
param authAllowedUserId string = ''

var suffix = uniqueString(resourceGroup().id)
var sqlServerName = '${namePrefix}-sql-${suffix}'
var sqlDbName = 'fitrecoverylog'
var storageName = toLower('${namePrefix}st${substring(suffix, 0, 8)}')
var planName = '${namePrefix}-plan'
var functionAppName = '${namePrefix}-api-${suffix}'
var aiName = '${namePrefix}-ai'

// ---- Azure SQL (serverless, free offering) ----

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// Allow other Azure services (the Function App) to reach the server.
resource sqlFirewallAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDbName
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    // Free offering: ~100k vCore-seconds + 32 GB/month, auto-pause when idle.
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    autoPauseDelay: 60
    minCapacity: json('0.5')
    zoneRedundant: false
  }
}

// ---- Storage (required by Functions) ----

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
  }
}

// ---- Application Insights ----

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: aiName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
  }
}

// ---- Functions (Linux Consumption, .NET isolated) ----

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  sku: { name: 'Y1', tier: 'Dynamic' }
  kind: 'functionapp'
  properties: { reserved: true } // Linux
}

var storageConn = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}'
var sqlConn = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${sqlDbName};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;'

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|9.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        { name: 'AzureWebJobsStorage', value: storageConn }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'SqlConnectionString', value: sqlConn }
        { name: 'AuthAuthority', value: authAuthority }
        { name: 'AuthAudience', value: authAudience }
        { name: 'AuthAllowedUserId', value: authAllowedUserId }
        // AuthDevBypass intentionally omitted in the cloud (defaults to off = enforce auth).
      ]
    }
  }
}

output functionAppName string = functionApp.name
output functionAppHostname string = functionApp.properties.defaultHostName
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDbName
