# Cloud sync backend — deploy runbook (Phase 1)

Stands up the Azure resources for the sync API. **Everything here is free-tier at personal
scale.** Auth uses a **personal Microsoft identity**, deliberately not the company tenant.

> Nothing is deployed yet. These are the steps to run when ready.

## Resources (`main.bicep`)

| Resource | Service | Free note |
|---|---|---|
| Database | Azure SQL (serverless, `useFreeLimit`) | ~100k vCore-sec + 32 GB/mo, auto-pauses when idle |
| API | Function App (Linux Consumption, .NET isolated 9) | 1M executions/mo free |
| Storage | Standard_LRS | Functions runtime requirement |
| Telemetry | Application Insights | free ingest allotment |

## Prerequisites

- Azure CLI: `az login` (personal subscription).
- A resource group: `az group create -n fitlog-rg -l eastus2`.
- Two app registrations in a **personal** Entra tenant / MSA (see step 1).

## 1. App registrations (auth)

App registrations are **Entra ID** objects (tenant-level), **not** resource-group resources.
Create them in a **personal tenant** — the default directory of your personal Azure
subscription — signed in as that subscription's owner. **Not** enchoice.com.

Note your **Directory (tenant) ID** (Entra ID → Overview) → this is `<YOUR_PERSONAL_TENANT_ID>`
in `authAuthority`.

### 1a. API app registration ("FitLog Sync API")

1. Entra ID → App registrations → **New registration**.
2. Name `FitLog Sync API`; supported accounts = **Accounts in this organizational directory only** (single tenant); no redirect URI. Register.
3. Copy **Application (client) ID** → this is `authAudience`.
4. **Expose an API** → set Application ID URI (accept default `api://<client-id>`) → **Add a scope** `access_as_user` (admins and users, enabled).

### 1b. Mobile client app registration ("FitLog iOS")

1. **New registration**, name `FitLog iOS (MAUI)`, same single-tenant option.
2. **Authentication** → Add a platform → **Mobile and desktop applications** → custom redirect URI:
   `msauth.com.mlawrence.fitrecoverylog://auth`. Set **Allow public client flows = Yes**.
3. **API permissions** → Add a permission → **My APIs** → *FitLog Sync API* → Delegated → `access_as_user` → **Grant admin consent**.
4. Copy this app's **client id** — the MAUI app (Phase 2) uses it as its MSAL client id.

### 1c. Your user object id (single-user lockdown)

Entra ID → Users → your user → **Object ID** → this is `authAllowedUserId`. (Or read the
`oid` claim from a token after first sign-in.)

## 2. Validate the template

```bash
az bicep build --file infra/main.bicep
cp infra/main.parameters.json.example infra/main.parameters.json   # fill in secrets (gitignored)
az deployment group what-if -g fitlog-rg -f infra/main.bicep -p @infra/main.parameters.json
```

## 3. Deploy

```bash
az deployment group create -g fitlog-rg -f infra/main.bicep -p @infra/main.parameters.json
```

Note the outputs (`functionAppHostname`, `sqlServerFqdn`).

## 4. Create the schema

The API does **not** auto-migrate on startup (unlike the phone). Apply the SQL Server
migration explicitly against the deployed DB:

```bash
export SqlConnectionString="Server=tcp:<sqlServerFqdn>,1433;Database=fitrecoverylog;User ID=<admin>;Password=<pwd>;Encrypt=True;"
dotnet dotnet-ef database update \
  --project src/FitRecoveryLog.Server \
  --startup-project src/FitRecoveryLog.Server
```

(Add your client IP to the SQL firewall first if running from your machine:
`az sql server firewall-rule create ...`.)

## 5. Publish the Functions app

```bash
cd src/FitRecoveryLog.Server
func azure functionapp publish <functionAppName>   # Azure Functions Core Tools
# or: dotnet publish -c Release  +  zip deploy
```

## 6. Smoke test

```bash
curl https://<functionAppHostname>/api/v1/ping        # 200, no auth
curl https://<functionAppHostname>/api/v1/sync?since=0 # 401 without a bearer token
```

## Hardening (later)

- **Managed identity for SQL** instead of an admin password in app settings (drop the
  password once the Function's identity is a DB user).
- **Server row-version cursor** instead of `UpdatedAt` (see design doc).
- **Tombstone purge** + the cursor-too-old full-resync path.
