#!/usr/bin/env bash
# =============================================
# Manfoods McDonald's — DB Update Script (SQL Server / MonsterASP)
# الاستخدام: bash scripts/db-update.sh
# =============================================
set -e

echo "⏳ جاري تطبيق التحديثات على قاعدة البيانات..."

# sqlcmd يتوقع مضيف/مستخدم/كلمة سر منفصلة، بعكس psql اللي كان بياخد رابط
# اتصال واحد. عشان SQLSERVER_CONNECTION_STRING (سلسلة ADO.NET كاملة) يفضل
# طريقة الإعداد الأساسية والوحيدة المطلوبة (تطابق أولوية BuildConnectionString
# في Program.cs)، الدالة دي بتفكّك السلسلة لاستخراج Server/Database/User/Password
# منها تلقائيًا بدل ما تحتاج تعرّف MSSQL_HOST/MSSQL_USER/... منفصلين كمان.
extract_field() {
  # $1 = connection string, $2 = اسم الحقل (regex غير حساس لحالة الأحرف)
  echo "$1" | tr ';' '\n' | grep -iE "^[[:space:]]*($2)[[:space:]]*=" | head -1 \
    | cut -d'=' -f2- | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//'
}

run_migration() {
  local server="$1" database="$2" user="$3" password="$4"
  sqlcmd -S "$server" -d "$database" -U "$user" -P "$password" -C -i scripts/migrate.sql
}

CONN="${SQLSERVER_CONNECTION_STRING:-$DATABASE_URL}"

if [ -n "$CONN" ]; then
  SERVER=$(extract_field "$CONN" "Server|Data Source|Addr|Address|Network Address")
  DATABASE=$(extract_field "$CONN" "Database|Initial Catalog")
  DB_USER=$(extract_field "$CONN" "User Id|UID|User")
  DB_PASSWORD=$(extract_field "$CONN" "Password|PWD")

  if [ -z "$SERVER" ] || [ -z "$DB_USER" ]; then
    echo "❌ تعذّر استخراج Server/User Id من SQLSERVER_CONNECTION_STRING."
    echo "   تأكد إنها بصيغة ADO.NET القياسية، مثال:"
    echo "   Server=HOST;Database=DB;User Id=USER;Password=PASS;"
    exit 1
  fi

  run_migration "$SERVER" "${DATABASE:-manfoods}" "$DB_USER" "$DB_PASSWORD"
elif [ -n "$MSSQL_HOST" ] && [ -n "$MSSQL_USER" ]; then
  run_migration "${MSSQL_HOST},${MSSQL_PORT:-1433}" "${MSSQL_DATABASE:-manfoods}" "$MSSQL_USER" "$MSSQL_PASSWORD"
else
  echo "❌ لا توجد بيانات اتصال بقاعدة البيانات."
  echo "   عرّف SQLSERVER_CONNECTION_STRING (أو MSSQL_HOST/MSSQL_USER بدلاً منها)."
  exit 1
fi

echo "✅ تم تحديث قاعدة البيانات بنجاح!"
