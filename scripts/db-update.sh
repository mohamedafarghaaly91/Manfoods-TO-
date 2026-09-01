#!/usr/bin/env bash
# =============================================
# Manfoods McDonald's — DB Update Script (SQL Server / MonsterASP)
# الاستخدام: bash scripts/db-update.sh
# =============================================
set -e

echo "⏳ جاري تطبيق التحديثات على قاعدة البيانات..."

# sqlcmd مش متوفر في بيئة Replit، فبنستخدم بدلاً منه أداة .NET بسيطة
# (Tools/DbMigrator) بتنفّذ scripts/migrate.sql عن طريق Microsoft.Data.SqlClient
# مباشرة — نفس الحزمة اللي المشروع أصلاً بيعتمد عليها عبر
# Microsoft.EntityFrameworkCore.SqlServer. الأداة دي بتقرأ نفس متغيرات البيئة
# اللي التطبيق نفسه بيقرأها (SQLSERVER_CONNECTION_STRING أولاً، بنفس أولوية
# BuildConnectionString في Program.cs)، فمفيش أي Secret إضافي مطلوب.
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

dotnet run --project "$REPO_ROOT/Tools/DbMigrator/DbMigrator.csproj" -- "$REPO_ROOT/scripts/migrate.sql"

echo "✅ تم تحديث قاعدة البيانات بنجاح!"
