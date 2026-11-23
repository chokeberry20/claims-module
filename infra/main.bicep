// Claims Module: walking-skeleton infrastructure (P2.5 task 17c).
// Deploy into an existing resource group:
//   az deployment group create -g <rg> -f infra/main.bicep -p infra/main.parameters.json
// The deploying identity needs Contributor plus permission to create role assignments
// (Owner, or Contributor + Role Based Access Control Administrator) on the resource group.

targetScope = 'resourceGroup'

@description('Lowercase prefix for all resource names (3-11 chars). Names must be globally unique, so pick something distinctive.')
@minLength(3)
@maxLength(11)
param namePrefix string

@description('Region for everything except the Static Web App.')
param location string = resourceGroup().location

@description('Static Web Apps is only offered in a few regions; westeurope, eastus2, centralus, westus2 and eastasia are valid.')
@allowed([
  'westeurope'
  'eastus2'
  'centralus'
  'westus2'
  'eastasia'
])
param staticWebAppLocation string = 'westeurope'

@description('Display name of the Microsoft Entra principal (user or group) that becomes the Azure SQL administrator.')
param sqlAdminLogin string

@description('Object ID of that Entra principal. Use a group that contains both you and the GitHub OIDC identity, so the pipeline can run migrations.')
param sqlAdminObjectId string

@description('Principal type of the SQL administrator.')
@allowed([
  'Group'
  'User'
  'Application'
])
param sqlAdminPrincipalType string = 'Group'

@description('Tags applied to every resource.')
param tags object = {
  project: 'claims-module'
  environment: 'assessment'
}

// ---------------------------------------------------------------------------
// Names
// ---------------------------------------------------------------------------
var suffix = uniqueString(resourceGroup().id)
var names = {
  logAnalytics: '${namePrefix}-logs'
  appInsights: '${namePrefix}-appi'
  plan: '${namePrefix}-plan'
  api: '${namePrefix}-api-${take(suffix, 6)}'
  swa: '${namePrefix}-web'
  sqlServer: '${namePrefix}-sql-${take(suffix, 6)}'
  sqlDatabase: 'ClaimsModule'
  storage: take('${namePrefix}st${suffix}', 24)
  keyVault: take('${namePrefix}-kv-${suffix}', 24)
}
var documentsContainer = 'claim-documents'

// Built-in role definition IDs.
var roles = {
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
}

// ---------------------------------------------------------------------------
// Monitoring
// ---------------------------------------------------------------------------
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: names.logAnalytics
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: names.appInsights
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ---------------------------------------------------------------------------
// Frontend: Static Web App (Free). Deployed by the pipeline with its deployment token.
// ---------------------------------------------------------------------------
resource staticWebApp 'Microsoft.Web/staticSites@2024-04-01' = {
  name: names.swa
  location: staticWebAppLocation
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

var frontendOrigin = 'https://${staticWebApp.properties.defaultHostname}'

// ---------------------------------------------------------------------------
// Azure SQL: Microsoft Entra-only authentication (no SQL passwords anywhere).
// The API connects with its managed identity; db/scripts/least-privilege-app-user.sql grants it access.
// ---------------------------------------------------------------------------
resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: names.sqlServer
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      principalType: sqlAdminPrincipalType
      tenantId: subscription().tenantId
    }
  }
}

// 0.0.0.0 - 0.0.0.0 is the documented "Allow Azure services" rule: the App Service reaches SQL over its public endpoint.
// The pipeline adds and removes a rule for the runner's IP around migrations.
resource sqlAllowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: names.sqlDatabase
  location: location
  tags: tags
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 2147483648
    requestedBackupStorageRedundancy: 'Local'
  }
}

// ---------------------------------------------------------------------------
// Storage: private blob container for claim documents (D-29, D-30).
// Shared-key access stays enabled because the baseline issues account-key SAS URLs (D-30).
// ---------------------------------------------------------------------------
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: names.storage
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource documents 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: documentsContainer
  properties: {
    publicAccess: 'None'
  }
}

// ---------------------------------------------------------------------------
// Key Vault (RBAC). Holds secrets the API reads through Key Vault references.
// P5 adds the JWT signing key, demo user passwords and the Hangfire dashboard password (D-32, D-41).
// ---------------------------------------------------------------------------
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: names.keyVault
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

resource storageConnectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'StorageConnectionString'
  properties: {
    value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
  }
}

// ---------------------------------------------------------------------------
// API: App Service B1 Linux. Always On keeps Hangfire's recurring jobs running (challenge 15).
// ---------------------------------------------------------------------------
resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: names.plan
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  properties: {
    reserved: true
  }
}

resource api 'Microsoft.Web/sites@2024-04-01' = {
  name: names.api
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    keyVaultReferenceIdentity: 'SystemAssigned'
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|9.0'
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/health/live'
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'Cors__AllowedOrigins__0', value: frontendOrigin }
        { name: 'Storage__Provider', value: 'AzureBlob' }
        { name: 'Storage__AzureBlob__ContainerName', value: documentsContainer }
        { name: 'Storage__AzureBlob__ConnectionString', value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=${storageConnectionSecret.name})' }
      ]
      connectionStrings: [
        {
          // Passwordless: the API authenticates to Azure SQL with its system-assigned managed identity.
          name: 'ClaimsDb'
          type: 'SQLAzure'
          connectionString: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${sqlDatabase.name};Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connect Timeout=30'
        }
      ]
    }
  }
}

// Read-only access to the vault's secrets for the API's managed identity (least privilege).
resource apiKeyVaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, api.id, roles.keyVaultSecretsUser)
  properties: {
    principalId: api.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsUser)
  }
}

// ---------------------------------------------------------------------------
// Outputs consumed by deploy.yml (no secrets).
// ---------------------------------------------------------------------------
output apiAppName string = api.name
output apiUrl string = 'https://${api.properties.defaultHostName}'
output apiPrincipalName string = api.name
output staticWebAppName string = staticWebApp.name
output frontendUrl string = frontendOrigin
output sqlServerName string = sqlServer.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDatabase.name
output storageAccountName string = storage.name
output keyVaultName string = keyVault.name
