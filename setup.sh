#!/usr/bin/env bash
set -e

echo "========================================="
echo "  Manfoods McDonald's - Project Setup"
echo "========================================="

# Check required secrets
if [ -z "$Gemini_API_Key" ]; then
  echo "⚠️  WARNING: Gemini_API_Key is not set. AI features will be disabled."
  echo "   Add it in the Replit Secrets tab."
else
  echo "✅ Gemini_API_Key is set."
fi

if [ -z "$NEON_DATABASE_URL" ]; then
  echo "ℹ️  NEON_DATABASE_URL not set — using Replit PostgreSQL (PGHOST/DATABASE_URL)."
else
  echo "✅ NEON_DATABASE_URL is set — using Neon database."
fi

echo ""
echo "Restoring NuGet packages..."
dotnet restore MvcApp.csproj

echo ""
echo "Starting application..."
dotnet run --project MvcApp.csproj
