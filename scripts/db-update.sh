#!/usr/bin/env bash
# =============================================
# Manfoods McDonald's — DB Update Script (SQL Server / MonsterASP)
# الاستخدام: bash scripts/db-update.sh
# =============================================
set -e

echo "⏳ جاري تطبيق التحديثات على قاعدة البيانات..."

# ترتيب مختلف عمدًا عن BuildConnectionString في Program.cs: sqlcmd (بعكس psql)
# يتوقع مضيف/مستخدم/كلمة سر منفصلة مش سلسلة اتصال واحدة، فـ MSSQL_HOST/MSSQL_USER
# هي المسار الأساسي هنا. لو عندك SQLSERVER_CONNECTION_STRING/DATABASE_URL فقط
# (سلسلة اتصال ADO.NET كاملة، وهي الأولوية داخل التطبيق نفسه)، شغّل sqlcmd يدويًا
# بدل هذا السكريبت، لأن تفكيك سلسلة الاتصال دي بأمان داخل bash غير موثوق.
if [ -n "$MSSQL_HOST" ] && [ -n "$MSSQL_USER" ]; then
  sqlcmd \
    -S "${MSSQL_HOST},${MSSQL_PORT:-1433}" \
    -d "${MSSQL_DATABASE:-manfoods}" \
    -U "$MSSQL_USER" \
    -P "$MSSQL_PASSWORD" \
    -C \
    -i scripts/migrate.sql
elif [ -n "$SQLSERVER_CONNECTION_STRING" ] || [ -n "$DATABASE_URL" ]; then
  echo "❌ SQLSERVER_CONNECTION_STRING/DATABASE_URL موجودة لكنها سلسلة اتصال كاملة."
  echo "   شغّل sqlcmd يدويًا بمفاتيح -S/-d/-U/-P المستخرجة منها، أو عرّف"
  echo "   MSSQL_HOST/MSSQL_PORT/MSSQL_DATABASE/MSSQL_USER/MSSQL_PASSWORD بدلاً منها."
  exit 1
else
  echo "❌ لا توجد بيانات اتصال بقاعدة البيانات."
  echo "   عرّف MSSQL_HOST/MSSQL_USER (و MSSQL_PORT/MSSQL_DATABASE/MSSQL_PASSWORD اختياريًا)."
  exit 1
fi

echo "✅ تم تحديث قاعدة البيانات بنجاح!"
