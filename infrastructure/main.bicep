// =============================================================================
// VaxTrace Cloud — Azure Infrastructure
// Deploy when you have Azure credits:
//   az deployment group create \
//     --resource-group vaxtrace-rg \
//     --template-file infrastructure/main.bicep \
//     --parameters sqlAdminPassword=YourSecurePass123!
// =============================================================================

targetScope = 'resourceGroup'

param location       string = resourceGroup().location
param environment    string = 'production'

@secure()
param sqlAdminPassword string
param sqlAdminLogin    string = 'sqladmin'

var prefix       = 'vaxtrace-${environment}'
var uniqueSuffix = uniqueString(resourceGroup().id)

// ── Storage Account (Queues + Blob) ───────────────────────────────────────────
resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name:     'vaxstorage${uniqueSuffix}'
  location: location
  sku:      { name: 'Standard_LRS' }
  kind:     'StorageV2'
  properties: {
    minimumTlsVersion:    'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

// ── Application Insights ──────────────────────────────────────────────────────
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name:     '${prefix}-insights'
  location: location
  kind:     'web'
  properties: { Application_Type: 'web' }
}

// ── Azure SQL ─────────────────────────────────────────────────────────────────
resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = {
  name:     '${prefix}-sql-${uniqueSuffix}'
  location: location
  properties: {
    administratorLogin:         sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion:          '1.2'
  }
}

resource sqlFirewall 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = {
  name:   'AllowAzureServices'
  parent: sqlServer
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2023-05-01-preview' = {
  name:     'VaxTraceDB'
  parent:   sqlServer
  location: location
  sku:      { name: 'S0', tier: 'Standard' }
  properties: { collation: 'SQL_Latin1_General_CP1_CI_AS' }
}

// ── Function App ──────────────────────────────────────────────────────────────
resource plan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name:     '${prefix}-plan'
  location: location
  sku:      { name: 'Y1', tier: 'Dynamic' }  // Consumption plan — pay per execution
}

resource functionApp 'Microsoft.Web/sites@2023-01-01' = {
  name:     '${prefix}-functions-${uniqueSuffix}'
  location: location
  kind:     'functionapp'
  properties: {
    serverFarmId: plan.id
    siteConfig: {
      netFrameworkVersion: 'v8.0'
      appSettings: [
        { name: 'AzureWebJobsStorage',                value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value}' }
        { name: 'FUNCTIONS_EXTENSION_VERSION',        value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME',           value: 'dotnet-isolated' }
        { name: 'VaxTraceStorage',                    value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value}' }
        { name: 'VaccinationQueueName',               value: 'vaccination-queue' }
        { name: 'VaccinationBlobContainer',           value: 'vaccination-raw-archive' }
        { name: 'ProcessedBlobContainer',             value: 'vaccination-processed' }
        { name: 'SqlConnectionString',                value: 'Server=${sqlServer.properties.fullyQualifiedDomainName};Database=VaxTraceDB;User Id=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
      ]
      ftpsState:     'Disabled'
      minTlsVersion: '1.2'
    }
    httpsOnly: true
  }
}

output functionAppName string = functionApp.name
output functionAppUrl  string = 'https://${functionApp.properties.defaultHostName}'
output sqlServerFqdn   string = sqlServer.properties.fullyQualifiedDomainName
output storageAccount  string = storage.name
