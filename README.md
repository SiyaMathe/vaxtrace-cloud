# VaxTrace Cloud
### Cloud-Native Vaccination Status Platform — Runs 100% Locally (No Azure Credits Required)

> **Skills Demonstrated:** Azure Functions (HTTP + Queue triggers) · Azure Storage Queues · Azure Blob Storage · Azure SQL Database · .NET 8 · C# · Docker · Azurite (local Azure emulator) · CI/CD · IaC (Bicep) · REST API design

---

## 🧭 What This Is

VaxTrace is a cloud backend platform that processes vaccination records from multiple providers into a central data store. It was built to demonstrate the full Azure cloud development stack from the CLDV6211/6212 curriculum — **without needing any Azure credits**.

Everything runs locally via Docker + Azurite. When you're ready for Azure, swap connection strings.

---

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     VAXTRACE CLOUD                          │
│                                                             │
│  POST /api/vaccination          (HTTP-trigger Function)     │
│       ↓                                                     │
│  Azure Storage Queue            (local: Azurite)            │
│  "vaccination-queue"                                        │
│       ↓                                                     │
│  Queue-trigger Function         (processes each message)    │
│       ↓                                                     │
│  ┌──────────────┐    ┌─────────────────────────────────┐   │
│  │ Azure SQL DB │    │ Blob Storage (raw JSON archive)  │   │
│  │ (local: MSSQL│    │ (local: Azurite blob container)  │   │
│  │  in Docker)  │    └─────────────────────────────────┘   │
│  └──────────────┘                                           │
│       ↓                                                     │
│  GET /api/vaccination/{id}      (HTTP-trigger Function)     │
│       → queries SQL, returns status in seconds              │
└─────────────────────────────────────────────────────────────┘
```

**Message formats supported (two different provider formats):**
```
Format A:  Id:VaccinationCenter:VaccinationDate:VaccineSerialNumber
Format B:  VaccineBarcode:VaccinationDate:VaccinationCenter:Id
```

---

## 📁 Project Structure

```
vaxtrace-cloud/
├── backend/
│   ├── functions/                        # Azure Functions (the core)
│   │   ├── HttpIngestFunction.cs         # POST — puts message on queue
│   │   ├── QueueProcessorFunction.cs     # Queue-trigger — saves to SQL + Blob
│   │   ├── HttpQueryFunction.cs          # GET — query vaccination status by ID
│   │   ├── HttpBulkIngestFunction.cs     # POST — bulk ingest multiple records
│   │   ├── MessageParser.cs             # Handles both message formats
│   │   ├── VaxTrace.Functions.csproj
│   │   └── local.settings.json.example  # Local dev config (Azurite)
│   ├── api/                              # Lightweight REST wrapper
│   │   └── ...
│   └── sql/
│       ├── 01_schema.sql                # Vaccination DB schema
│       ├── 02_stored_procedures.sql     # Ingest + query procedures
│       └── 03_seed_data.sql             # Test vaccination records
├── scripts/
│   ├── setup-local.sh                   # One-command local setup
│   └── test-endpoints.sh                # Test all endpoints locally
├── tests/
│   ├── VaxTrace.Tests.csproj
│   ├── MessageParserTests.cs
│   └── FunctionTests.cs
├── infrastructure/
│   └── main.bicep                       # Azure IaC (for when you get credits)
├── .github/workflows/
│   ├── ci.yml
│   └── cd.yml
├── .vscode/
│   ├── extensions.json
│   └── launch.json
├── docker-compose.yml                   # SQL Server + Azurite
├── .env.example
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
| VS Code Extensions | Open project → `Ctrl+Shift+P` → "Extensions: Show Recommended" → Install All |

### Step 1 — Clone & configure
```bash
git clone https://github.com/SiyaMathe/vaxtrace-cloud.git
cd vaxtrace-cloud

# Copy local settings
cp backend/functions/local.settings.json.example backend/functions/local.settings.json
cp .env.example .env
```

### Step 2 — Start local Azure emulator + SQL Server
```bash
docker-compose up -d
# This starts:
#   • Azurite on ports 10000 (blob), 10001 (queue), 10002 (table)
#   • SQL Server 2022 on port 1433
#   • A db-init container that applies the schema automatically
```

### Step 3 — Start the Azure Functions
```bash
cd backend/functions
func start
# Functions available at http://localhost:7071
```

### Step 4 — Test the endpoints
```bash
# From a new terminal
chmod +x scripts/test-endpoints.sh
./scripts/test-endpoints.sh
```

Or use the VS Code REST Client extension with `requests.http`.

---

## 📬 API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET`  | `/api/health` | Health check |
| `POST` | `/api/vaccination` | Submit a vaccination record (puts on queue) |
| `POST` | `/api/vaccination/bulk` | Submit multiple records |
| `GET`  | `/api/vaccination/{id}` | Query status by SA ID or passport number |
| `GET`  | `/api/vaccination/stats` | Queue + ingestion stats |

### Example: Submit a vaccination record
```bash
curl -X POST http://localhost:7071/api/vaccination \
  -H "Content-Type: application/json" \
  -d '{
    "format": "A",
    "id": "0105215359081",
    "vaccinationCenter": "Groote Schuur Hospital",
    "vaccinationDate": "2024-01-15",
    "vaccineSerialNumber": "PFZ-2024-001-A"
  }'
```

### Example: Query by ID
```bash
curl http://localhost:7071/api/vaccination/0105215359081
```

---

## ☁️ Deploy to Azure (When You Get Credits)

```bash
# Login
az login

# Create resource group
az group create --name vaxtrace-rg --location southafricanorth

# Deploy infrastructure
az deployment group create \
  --resource-group vaxtrace-rg \
  --template-file infrastructure/main.bicep \
  --parameters sqlAdminPassword=YourSecurePassword123!

# Add GitHub secrets then push to main — CI/CD handles the rest
git push origin main
```

### Required GitHub Secrets (for CD pipeline)
| Secret | Source |
|--------|--------|
| `AZURE_CREDENTIALS` | `az ad sp create-for-rbac --sdk-auth` |
| `AZURE_FUNCTIONAPP_NAME` | Output from Bicep deployment |
| `AZURE_SQL_CONNECTION_STRING` | Azure Portal → SQL Database |
| `AZURE_STORAGE_CONNECTION_STRING` | Azure Portal → Storage Account |

---

## 🧪 Running Tests

```bash
cd tests
dotnet test --verbosity normal
```
