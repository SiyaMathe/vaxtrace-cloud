# VaxTrace Cloud
### Cloud-Native Vaccination Status Platform — Runs 100% Locally (No Azure Credits Required)

> **Skills Demonstrated:** Azure Functions (HTTP + Queue triggers) · Azure Storage Queues · Azure Blob Storage · Azure SQL Database · .NET 8 · C# · Docker · Azurite (local Azure emulator) · CI/CD · IaC (Bicep) · REST API design

---

## 🧭 What This Is

VaxTrace Cloud is a **cloud-native vaccination record processing platform** that ingests vaccination data from multiple providers with different message formats, routes records through an Azure Storage Queue for asynchronous processing, archives raw payloads to Blob Storage, and persists structured records to an Azure SQL database — all queryable in seconds via an HTTP endpoint.

The system is designed to run **entirely locally using Docker and Azurite** (Microsoft's official Azure Storage emulator), eliminating the need for cloud credits while producing architecture and code identical to a real Azure deployment. When credits become available, swapping connection strings and pushing to `main` triggers the full CI/CD pipeline.

---

## 🗄️ Database Design (ERD)

The system uses a fully normalised schema to manage vaccination records, audit logs, and provider data. Below is the Entity Relationship Diagram (ERD):

![VaxTrace Cloud ERD](VaxTraceDB%20-%20VaxTraceDB%20-%20dbo.png)

---
## 🏗️ Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│                        VAXTRACE CLOUD                            │
│                                                                  │
│  POST /api/vaccination           (HTTP-trigger Function)         │
│       ↓                                                          │
│  Azure Storage Queue             (local: Azurite port 10001)     │
│  "vaccination-queue"                                             │
│       ↓  (fires automatically on message arrival)                │
│  Queue-trigger Function          (QueueProcessorFunction.cs)     │
│       ↓                      ↓                                   │
│  ┌──────────────────┐   ┌─────────────────────────────────────┐  │
│  │  Azure SQL DB    │   │  Blob Storage (raw JSON archive)    │  │
│  │  VaccinationRecord   │  vaccination-raw-archive/{date}/    │  │
│  │  QueueMessageLog │   │  format{A|B}/{id}_{timestamp}.json  │  │
│  │  (local: Docker) │   │  (local: Azurite port 10000)        │  │
│  └──────────────────┘   └─────────────────────────────────────┘  │
│       ↓                                                          │
│  GET /api/vaccination/{id}       (HTTP-trigger Function)         │
│       → queries SQL, returns full dose history in < 1 second     │
└──────────────────────────────────────────────────────────────────┘
```

**Two provider message formats supported:**
```
Format A:  Id:VaccinationCenter:VaccinationDate:VaccineSerialNumber
           8001015009087:Groote Schuur Hospital:2024-01-15:PFZ-2024-001-A

Format B:  VaccineBarcode:VaccinationDate:VaccinationCenter:Id
           BAR-00123:2024-01-15:Groote Schuur Hospital:8001015009087
```

---

## 📁 Project Structure

```
vaxtrace-cloud/
├── backend/
│   ├── functions/
│   │   ├── HttpIngestFunction.cs          # POST /api/vaccination — validates & queues message
│   │   ├── QueueProcessorFunction.cs      # Queue-trigger — archives to Blob, upserts to SQL
│   │   ├── HttpQueryFunction.cs           # GET /api/vaccination/{id} — status lookup
│   │   ├── HealthStatsAndBulkFunctions.cs # Health, Stats, and Bulk ingest endpoints
│   │   ├── MessageParser.cs               # Format A & B detection and parsing
│   │   ├── Program.cs                     # Functions host setup and DI
│   │   ├── host.json                      # Queue polling, retry, and encoding config
│   │   ├── VaxTrace.Functions.csproj
│   │   └── local.settings.json.example    # Local dev config (Azurite connection strings)
│   └── sql/
│       ├── 01_schema.sql                  # Normalised schema: Person, VaccinationRecord, QueueMessageLog
│       ├── 02_stored_procedures.sql       # Upsert, query, log, stats procedures
│       └── 03_seed_data.sql               # Hard-coded test IDs and pre-seeded records
├── scripts/
│   ├── setup-local.sh                     # One-command prerequisite check + stack setup
│   └── test-endpoints.sh                  # curl-based endpoint test suite
├── tests/
│   ├── VaxTrace.Tests.csproj
│   └── MessageParserTests.cs              # 17 unit tests: Format A/B, edge cases, round-trips
├── infrastructure/
│   └── main.bicep                         # Azure IaC — Function App, SQL, Storage, App Insights
├── .github/
│   └── workflows/
│       ├── ci.yml                         # Build, unit tests, integration tests (Azurite + SQL Server)
│       └── cd.yml                         # Bicep deploy → schema migration → Function deploy → smoke test
├── .vscode/
│   ├── extensions.json                    # Recommended extensions
│   ├── launch.json                        # Debug configs for Functions and tests
│   ├── tasks.json                         # Build, docker-up, run-tests tasks
│   └── settings.json                      # mssql connection to local Docker SQL Server
├── requests.http                          # VS Code REST Client — all endpoints ready to fire
├── docker-compose.yml                     # SQL Server 2022 + Azurite + db-init + queue-init
├── .env.example
├── .gitignore
└── README.md
```

---

## 🚀 Local Setup (No Azure Credits Needed)

### Prerequisites — install these once

| Tool | Install |
|------|---------|
| Docker Desktop | https://www.docker.com/products/docker-desktop |
| .NET 8 SDK | https://dotnet.microsoft.com/download/dotnet/8.0 |
| Azure Functions Core Tools v4 | `npm install -g azure-functions-core-tools@4` |
| VS Code | https://code.visualstudio.com |
| VS Code Extensions | Open project → `Ctrl+Shift+P` → "Extensions: Show Recommended Extensions" → Install All |

### Step 1 — Clone & configure

```bash
git clone https://github.com/SiyaMathe/vaxtrace-cloud.git
cd vaxtrace-cloud

# Copy local settings (uses Azurite + Docker SQL by default)
cp backend/functions/local.settings.json.example backend/functions/local.settings.json
cp .env.example .env
```

### Step 2 — Start the full local stack

```bash
docker-compose up -d
```

This single command starts four containers:

| Container | Purpose | Port |
|-----------|---------|------|
| `vaxtrace-azurite` | Azure Storage emulator (Blob + Queue + Table) | 10000, 10001, 10002 |
| `vaxtrace-sql` | SQL Server 2022 Developer Edition (free) | 1433 |
| `vaxtrace-db-init` | Applies all three SQL files automatically, then exits | — |
| `vaxtrace-queue-init` | Pre-creates queues and blob containers in Azurite, then exits | — |

Wait ~30 seconds for SQL Server to be ready, then verify:

```bash
docker ps   # vaxtrace-azurite and vaxtrace-sql should show "Up"
```

### Step 3 — Restore packages and run tests

```bash
dotnet restore backend/functions/VaxTrace.Functions.csproj
dotnet test tests/VaxTrace.Tests.csproj --verbosity normal
```

All 17 unit tests should pass without any services running (they test the parser only).

### Step 4 — Start the Azure Functions

```bash
cd backend/functions
func start
```

You will see five functions register in the console:

```
Functions:
  Health:                    [GET]  http://localhost:7071/api/health
  HttpIngestVaccination:     [POST] http://localhost:7071/api/vaccination
  HttpBulkIngestVaccination: [POST] http://localhost:7071/api/vaccination/bulk
  HttpQueryVaccination:      [GET]  http://localhost:7071/api/vaccination/{id}
  VaccinationStats:          [GET]  http://localhost:7071/api/vaccination/stats
  QueueProcessorFunction:    queueTrigger
```

### Step 5 — Test the endpoints

**Option A — VS Code REST Client (recommended)**

Open `requests.http` in VS Code, install the `humao.rest-client` extension, then click **Send Request** above any block.

**Option B — Shell script**

```bash
# From a new terminal (keep func start running)
chmod +x scripts/test-endpoints.sh
./scripts/test-endpoints.sh
```

**Option C — Quick curl test**

```bash
# Health check
curl http://localhost:7071/api/health

# Query a pre-seeded fully vaccinated ID
curl http://localhost:7071/api/vaccination/0105215258021
```

---

## 📬 API Reference

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| `GET`  | `/api/health` | Anonymous | Service health — checks SQL and queue connectivity |
| `POST` | `/api/vaccination` | Anonymous | Submit one vaccination record (Format A or B) |
| `POST` | `/api/vaccination/bulk` | Anonymous | Submit an array of records |
| `GET`  | `/api/vaccination/{id}` | Anonymous | Query full vaccination status by SA ID or passport |
| `GET`  | `/api/vaccination/stats` | Anonymous | Queue throughput and vaccination coverage stats |

### POST /api/vaccination — JSON body (Format A)

```bash
curl -X POST http://localhost:7071/api/vaccination \
  -H "Content-Type: application/json" \
  -d '{
    "format": "A",
    "id": "0105215258021",
    "vaccinationCenter": "Groote Schuur Hospital",
    "vaccinationDate": "2024-01-15",
    "vaccineSerialNumber": "PFZ-2024-001-A"
  }'
```

**Response (202 Accepted):**
```json
{
  "status": "queued",
  "messageId": "...",
  "detectedFormat": "A",
  "idNumber": "0105215258021",
  "center": "Groote Schuur Hospital",
  "date": "2024-01-15",
  "message": "Record queued for processing. Query status at GET /api/vaccination/{id}"
}
```

### POST /api/vaccination — raw message string (Format B)

```bash
curl -X POST http://localhost:7071/api/vaccination \
  -H "Content-Type: text/plain" \
  -d "BAR-00001:2024-02-12:Groote Schuur Hospital:0105215258021"
```

### GET /api/vaccination/{id}

```bash
curl http://localhost:7071/api/vaccination/0105215258021
```

**Response (200 OK):**
```json
{
  "status": "FULLY_VACCINATED",
  "idNumber": "0105215258021",
  "idType": "SA_ID",
  "name": "Siyabulela Mathe",
  "vaccination": {
    "totalDoses": 2,
    "isFullyVaccinated": true,
    "firstDoseDate": "2024-01-15",
    "latestDoseDate": "2024-02-12",
    "daysSinceLastDose": 108
  },
  "doses": [
    {
      "doseNumber": 1,
      "vaccinationDate": "2024-01-15",
      "vaccinationCenter": "Groote Schuur Hospital",
      "serialNumber": "PFZ-2024-001-A",
      "providerFormat": "A",
      "isVerified": true
    },
    {
      "doseNumber": 2,
      "vaccinationDate": "2024-02-12",
      "vaccinationCenter": "Groote Schuur Hospital",
      "barcode": "BAR-00001",
      "providerFormat": "B",
      "isVerified": true
    }
  ]
}
```

### POST /api/vaccination/bulk

```bash
curl -X POST http://localhost:7071/api/vaccination/bulk \
  -H "Content-Type: application/json" \
  -d '[
    "8001015009087:Charlotte Maxeke Hospital:2024-01-20:JNJ-2024-002-A",
    "BAR-00999:2024-03-05:Steve Biko Academic Hospital:0407145189089",
    "P12345678:Groote Schuur Hospital:2024-04-10:AZ-2024-200"
  ]'
```

---

## 🗄️ Database Schema

The SQL schema is fully normalised (3NF) with three core tables:

```
Person (1) ────────────< VaccinationRecord (*)
                              ↓
                         QueueMessageLog (audit trail)
```

| Table | Purpose |
|-------|---------|
| `Person` | Identity anchor — one row per SA ID or passport number |
| `VaccinationRecord` | One row per dose event with historical provider data snapshot |
| `VaccinationCenter` | Lookup — normalised center names seeded from incoming messages |
| `Vaccine` | Lookup — vaccine product catalogue |
| `QueueMessageLog` | Complete audit trail of every queue message received and its outcome |

**Key stored procedures:**

| Procedure | What it does |
|-----------|-------------|
| `usp_UpsertVaccinationRecord` | Idempotent MERGE-based insert — replaying a duplicate message is a no-op |
| `usp_GetVaccinationStatus` | Returns two result sets: person summary + all dose records |
| `usp_LogQueueMessage` | Audit log insert at message receipt |
| `usp_UpdateQueueMessageLog` | Updates log with SUCCESS, DUPLICATE, or FAILED outcome |
| `usp_GetProcessingStats` | Returns three result sets: queue stats, recent failures, coverage counts |

---

## 🔄 Message Processing Pipeline

When a message hits the queue, `QueueProcessorFunction` runs this pipeline:

```
1. Receive message from "vaccination-queue"
       ↓
2. Parse: detect Format A or B via MessageParser.cs
       ↓
3. Log to QueueMessageLog (SQL) — status: RECEIVED
       ↓
4. Archive raw JSON to Blob Storage
       path: {year}/{month}/{day}/format{A|B}/{id}_{HHmmss}.json
       ↓
5. Call usp_UpsertVaccinationRecord (SQL stored procedure)
       — idempotent: duplicate = no new row, returns existing RecordID
       ↓
6. Update QueueMessageLog — status: SUCCESS or DUPLICATE
       ↓
   On any failure → status: FAILED → Azure retries up to 5 times
                  → after 5 retries → dead-letter queue
```

---

## 🧪 Running Tests

```bash
# Unit tests only (no services needed)
dotnet test tests/VaxTrace.Tests.csproj --verbosity normal

# Integration tests (requires docker-compose up -d first)
dotnet test tests/VaxTrace.Tests.csproj --filter Category=Integration
```

**Test coverage:**

| Test class | Count | What's tested |
|-----------|-------|--------------|
| `MessageParserTests` | 17 | Format A parsing, Format B parsing, passport numbers, center names with colons, invalid dates, null/empty input, round-trip build→parse, all 5 seed messages as a Theory |

---

## ☁️ Deploy to Azure (When You Get Credits)

### Step 1 — Provision infrastructure

```bash
az login

az group create \
  --name vaxtrace-rg \
  --location southafricanorth

az deployment group create \
  --resource-group vaxtrace-rg \
  --template-file infrastructure/main.bicep \
  --parameters sqlAdminPassword=YourSecurePassword123!
```

The Bicep template provisions on the **Consumption plan** (pay-per-execution — effectively free at low volume):
- Azure Function App
- Azure SQL Database (S0 tier)
- Azure Storage Account (queues + blob containers)
- Application Insights

### Step 2 — Add GitHub Secrets

Go to your repo → **Settings → Secrets and variables → Actions** → New repository secret:

| Secret | How to get it |
|--------|--------------|
| `AZURE_CREDENTIALS` | `az ad sp create-for-rbac --sdk-auth --role contributor --scopes /subscriptions/<id>` |
| `AZURE_RESOURCE_GROUP` | `vaxtrace-rg` |
| `AZURE_FUNCTIONAPP_NAME` | From Bicep output: `functionAppName` |
| `SQL_SERVER_NAME` | From Bicep output: `sqlServerFqdn` (without `.database.windows.net`) |
| `SQL_ADMIN_USER` | `sqladmin` |
| `SQL_ADMIN_PASSWORD` | The password you chose above |

### Step 3 — Push to main

```bash
git push origin main
```

The CD pipeline runs automatically:
1. Deploys Bicep infrastructure
2. Applies SQL schema to Azure SQL
3. Publishes and deploys the Function App
4. Runs a smoke test against the live `/api/health` endpoint

---

## 🔍 Viewing Data Locally

### Azure Storage Explorer (free desktop app)
1. Download from https://azure.microsoft.com/features/storage-explorer
2. Connect → Local emulator → **Use development storage**
3. Browse queues: `vaccination-queue`, `vaccination-deadletter`
4. Browse blob containers: `vaccination-raw-archive`, `vaccination-processed`

### SQL Server in VS Code
1. Install the **mssql** extension (in `.vscode/extensions.json`)
2. Press `Ctrl+Shift+P` → **MS SQL: Connect**
3. Use the pre-configured connection: **VaxTrace Local (Docker)**
4. Password: `VaxTrace_Dev123!`

---

## 📋 Pre-seeded Test IDs

The following SA ID numbers are seeded in `03_seed_data.sql` and ready to query immediately:

| SA ID | Name | Status |
|-------|------|--------|
| `0105215258021` | Siyabulela Mathe | Fully vaccinated (2 doses) |
| `8001015009087` | Thabo Nkosi | Partially vaccinated (1 dose — J&J) |
| `9203224800088` | Ayanda Dube | Fully vaccinated (2 doses — AstraZeneca) |
| `7512086150082` | Lerato Molefe | Not vaccinated (person record only) |
| `0407145189089` | Amahle Zulu | Not vaccinated (person record only) |
| `P12345678` | James Smith | Not vaccinated (passport — foreign national) |

---

## 🛠️ Troubleshooting

**`func start` fails with "No job functions found"**
```bash
dotnet build backend/functions/VaxTrace.Functions.csproj
# Then retry: func start
```

**SQL connection refused**
```bash
docker ps   # check vaxtrace-sql is running
docker logs vaxtrace-sql   # check for startup errors
# SQL Server takes ~30s to be ready after first start
```

**Azurite queue not found**
```bash
docker logs vaxtrace-queue-init   # check if queue creation ran
# If it failed, re-run:
docker-compose up queue-init
```

**Port 1433 already in use**
```bash
# Stop any local SQL Server instance, or change the port in docker-compose.yml
# Change: "1433:1433" to "1434:1433"
# And update local.settings.json: Server=localhost,1434;...
```
