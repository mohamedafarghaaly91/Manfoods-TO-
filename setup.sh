#!/usr/bin/env bash
set -e

echo "========================================="
echo "  Manfoods McDonald's - Starting up"
echo "========================================="

# ── [1/4] Check required configuration ────────
echo ""
echo "[1/4] Checking configuration..."

DB_OK=false
if [ -n "$NEON_DATABASE_URL" ]; then
  echo "  ✅ NEON_DATABASE_URL is set — using Neon database."
  DB_OK=true
elif [ -n "$PGHOST" ] && [ -n "$PGUSER" ]; then
  echo "  ✅ PGHOST/PGUSER are set — using Replit PostgreSQL."
  DB_OK=true
elif [ -n "$DATABASE_URL" ]; then
  echo "  ✅ DATABASE_URL is set."
  DB_OK=true
fi

if [ "$DB_OK" = false ]; then
  echo ""
  echo "  ❌ ERROR: No database URL found."
  echo "     Please set NEON_DATABASE_URL in Configurations (Secrets tab)."
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
