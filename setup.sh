#!/usr/bin/env bash
set -e

echo "========================================="
echo "  Manfoods McDonald's - Starting up"
echo "========================================="

# ── [1/4] Check required configuration ────────
echo ""
echo "[1/4] Checking configuration..."

DB_OK=false
if [ -n "$SQLSERVER_CONNECTION_STRING" ]; then
  echo "  ✅ SQLSERVER_CONNECTION_STRING is set — using it directly."
  DB_OK=true
elif [ -n "$MSSQL_HOST" ] && [ -n "$MSSQL_USER" ]; then
  echo "  ✅ MSSQL_HOST/MSSQL_USER are set — using MonsterASP SQL Server."
  DB_OK=true
elif [ -n "$DATABASE_URL" ]; then
  echo "  ✅ DATABASE_URL is set."
  DB_OK=true
fi

if [ "$DB_OK" = false ]; then
  echo ""
  echo "  ❌ ERROR: No SQL Server connection info found."
  echo "     Please set SQLSERVER_CONNECTION_STRING (or MSSQL_HOST/MSSQL_PORT/"
  echo "     MSSQL_DATABASE/MSSQL_USER/MSSQL_PASSWORD) in Configurations (Secrets tab)."
  exit 1
fi

# ── [2/4] Restore NuGet packages ──────────────
echo ""
echo "[2/4] Restoring NuGet packages..."
dotnet restore MvcApp.csproj --nologo -v q
echo "  ✅ Packages restored."

# ── [3/4] Push database schema ────────────────
echo ""
echo "[3/4] Pushing database schema..."
bash scripts/db-update.sh
echo "  ✅ Schema is up to date."

# ── [4/4] Start application ───────────────────
echo ""
echo "[4/4] Starting application..."
echo ""
echo "========================================="
echo "  App: https://$REPLIT_DEV_DOMAIN/"
echo "========================================="
echo ""

dotnet run --project MvcApp.csproj
