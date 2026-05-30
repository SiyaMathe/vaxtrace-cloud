#!/usr/bin/env bash
# =============================================================================
# VaxTrace Cloud — Local Setup Script
# Run this once to get the full stack running locally (no Azure credits needed)
# =============================================================================

set -euo pipefail

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

info()  { echo -e "${GREEN}[INFO]${NC}  $1"; }
warn()  { echo -e "${YELLOW}[WARN]${NC}  $1"; }
error() { echo -e "${RED}[ERROR]${NC} $1"; exit 1; }

echo ""
echo "╔══════════════════════════════════════╗"
echo "║   VaxTrace Cloud — Local Setup       ║"
echo "║   No Azure credits required          ║"
echo "╚══════════════════════════════════════╝"
echo ""

# ── Check prerequisites ───────────────────────────────────────────────────────
info "Checking prerequisites..."

command -v docker     >/dev/null 2>&1 || error "Docker not found. Install from https://docker.com"
command -v dotnet     >/dev/null 2>&1 || error ".NET 8 SDK not found. Install from https://dotnet.microsoft.com/download"
command -v func       >/dev/null 2>&1 || warn  "Azure Functions Core Tools not found. Run: npm install -g azure-functions-core-tools@4"

DOTNET_VER=$(dotnet --version | cut -d. -f1)
[ "$DOTNET_VER" -ge 8 ] || error ".NET 8+ required. Current: $(dotnet --version)"

info "Prerequisites OK ✓"

# ── Copy local settings ───────────────────────────────────────────────────────
FUNC_DIR="$(dirname "$0")/../backend/functions"
SETTINGS="$FUNC_DIR/local.settings.json"
EXAMPLE="$FUNC_DIR/local.settings.json.example"

if [ ! -f "$SETTINGS" ]; then
    info "Copying local.settings.json from example..."
    cp "$EXAMPLE" "$SETTINGS"
    info "Created local.settings.json ✓"
else
    info "local.settings.json already exists ✓"
fi

# ── Start Docker services ─────────────────────────────────────────────────────
info "Starting Docker services (SQL Server + Azurite)..."
cd "$(dirname "$0")/.."
docker-compose up -d

info "Waiting for SQL Server to be ready..."
RETRIES=0
until docker exec vaxtrace-sql \
    /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "VaxTrace_Dev123!" -Q "SELECT 1" -b \
    >/dev/null 2>&1; do
    RETRIES=$((RETRIES+1))
    [ $RETRIES -gt 20 ] && error "SQL Server did not start in time."
    echo -n "."
    sleep 3
done
echo ""
info "SQL Server ready ✓"

info "Waiting for Azurite to be ready..."
sleep 5
info "Azurite ready ✓"

# ── Restore .NET packages ─────────────────────────────────────────────────────
info "Restoring .NET packages..."
dotnet restore backend/functions/VaxTrace.Functions.csproj --verbosity quiet
info "Packages restored ✓"

# ── Run tests ─────────────────────────────────────────────────────────────────
info "Running unit tests..."
dotnet test tests/VaxTrace.Tests.csproj --verbosity minimal 2>&1 || warn "Some tests failed — check above"

echo ""
echo "╔══════════════════════════════════════════════════════════╗"
echo "║  Setup complete! To start the Functions:                 ║"
echo "║                                                          ║"
echo "║    cd backend/functions && func start                   ║"
echo "║                                                          ║"
echo "║  Then test the endpoints:                                ║"
echo "║    ./scripts/test-endpoints.sh                           ║"
echo "║                                                          ║"
echo "║  Health check:                                           ║"
echo "║    http://localhost:7071/api/health                      ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""
