// Standalone SQL Server migration runner — replaces sqlcmd, which isn't
// available in the Replit environment. Executes scripts/migrate.sql (or
// whatever .sql file is passed as the first argument) against the database
// named by the exact same connection-string resolution order Program.cs uses,
// so no secrets beyond what the web app already needs (SQLSERVER_CONNECTION_STRING
// alone is enough) are required.
//
// Usage: dotnet run --project Tools/DbMigrator -- scripts/migrate.sql

using Microsoft.Data.SqlClient;

var scriptPath = args.Length > 0 ? args[0] : Path.Combine("scripts", "migrate.sql");
var fullScriptPath = Path.GetFullPath(scriptPath, Environment.CurrentDirectory);

if (!File.Exists(fullScriptPath))
{
    Console.Error.WriteLine($"❌ Script not found: {fullScriptPath}");
    return 1;
}

string connectionString;
try
{
    connectionString = BuildConnectionString();
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"❌ {ex.Message}");
    return 1;
}

var sql = await File.ReadAllTextAsync(fullScriptPath);

// "GO" is a client-side batch separator (sqlcmd/SSMS convention), not real
// T-SQL — SqlCommand has no concept of it, so batches must be split and
// executed one at a time here. scripts/migrate.sql currently has none (no
// CREATE VIEW/PROCEDURE/etc. requiring a separate batch), so this is a
// single-batch no-op today, but keeps the tool correct if that ever changes.
var batches = sql
    .Split('\n')
    .Aggregate(new List<System.Text.StringBuilder> { new() }, (acc, line) =>
    {
        if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            acc.Add(new System.Text.StringBuilder());
        else
            acc[^1].AppendLine(line);
        return acc;
    })
    .Select(b => b.ToString())
    .Where(b => !string.IsNullOrWhiteSpace(b))
    .ToList();

Console.WriteLine($"⏳ Applying {Path.GetFileName(fullScriptPath)} ({batches.Count} batch(es))...");

try
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    foreach (var batch in batches)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = batch;
        command.CommandTimeout = 300;
        await command.ExecuteNonQueryAsync();
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"❌ Migration failed: {ex.Message}");
    return 1;
}

Console.WriteLine("✅ Database updated successfully!");
return 0;

// Mirrors Program.cs's BuildConnectionString exactly (same env vars, same
// priority order) so this tool and the web app never disagree about which
// database they're pointed at. Kept as a separate copy rather than a shared
// reference because Program.cs's version is a top-level-statement local
// function (not visible outside MvcApp), and this tool is deliberately a
// minimal, standalone console app with no dependency on the full web project.
static string BuildConnectionString()
{
    var fullConnectionString = Environment.GetEnvironmentVariable("SQLSERVER_CONNECTION_STRING");
    if (!string.IsNullOrEmpty(fullConnectionString))
        return fullConnectionString;

    var mssqlHost = Environment.GetEnvironmentVariable("MSSQL_HOST");
    var mssqlPort = Environment.GetEnvironmentVariable("MSSQL_PORT") ?? "1433";
    var mssqlDatabase = Environment.GetEnvironmentVariable("MSSQL_DATABASE");
    var mssqlUser = Environment.GetEnvironmentVariable("MSSQL_USER");
    var mssqlPassword = Environment.GetEnvironmentVariable("MSSQL_PASSWORD");

    if (!string.IsNullOrEmpty(mssqlHost) && !string.IsNullOrEmpty(mssqlUser))
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"{mssqlHost},{mssqlPort}",
            InitialCatalog = mssqlDatabase,
            UserID = mssqlUser,
            Password = mssqlPassword,
            Encrypt = true,
            TrustServerCertificate = Environment.GetEnvironmentVariable("MSSQL_TRUST_SERVER_CERTIFICATE") == "true",
        };
        return builder.ConnectionString;
    }

    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (!string.IsNullOrEmpty(databaseUrl))
        return databaseUrl;

    throw new InvalidOperationException(
        "No database connection info found. Set SQLSERVER_CONNECTION_STRING " +
        "(or MSSQL_HOST/MSSQL_PORT/MSSQL_DATABASE/MSSQL_USER/MSSQL_PASSWORD).");
}
