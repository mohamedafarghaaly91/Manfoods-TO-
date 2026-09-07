---
name: Preview database provider mismatch
description: The preview startup path can receive a PostgreSQL-style DATABASE_URL while the imported MVC app is wired to SQL Server.
---

The preview startup check treats `DATABASE_URL` as sufficient, but both the web app and the migration helper pass it to Microsoft.Data.SqlClient. A PostgreSQL URI therefore fails before the web server starts.

**Why:** The configured environment exposed a PostgreSQL-style DATABASE_URL while this project was migrated from a SQL Server deployment and still uses EF Core SQL Server.

**How to apply:** Before changing database code or startup scripts, confirm the intended provider and preserve the existing SQL Server path. Do not expose or persist the connection value.