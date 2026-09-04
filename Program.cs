using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Extensions;
using MvcApp.Services;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

builder.Services.AddLocalization(opts => opts.ResourcesPath = "");
builder.Services.AddControllersWithViews()
    .AddViewLocalization()
    // DataAnnotations ErrorMessage strings on the view models are resx keys,
    // resolved against the same SharedResource pair the views use.
    .AddDataAnnotationsLocalization(o => o.DataAnnotationLocalizerProvider =
        (type, factory) => factory.Create(typeof(MvcApp.Resources.SharedResource)));

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

builder.Services.AddRateLimiter(options =>
{
    // Partitioned per client IP — a single shared/global bucket here would let
    // any one anonymous caller exhaust the entire app's login budget and lock
    // every user (including Admin) out of authenticating. Same pattern as the
    // "api" policy below; ForwardedHeadersOptions above already resolves the
    // real client IP behind the reverse proxy. Applies to /login, /adminlogin,
    // Forgot Password, and Admin Recover (all carry [EnableRateLimiting("login")]).
    options.AddPolicy("login", context =>
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(key, _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 10,
            QueueLimit = 0
        });
    });

    // Unlike "login" above (now per-IP too), the dashboard API surface gets
    // many parallel requests per page load from every logged-in user, so it
    // must be partitioned per client IP rather than sharing a single global
    // budget — otherwise a handful of concurrent users would exhaust it for
    // everyone. ForwardedHeadersOptions above already resolves the real
    // client IP behind the reverse proxy.
    options.AddPolicy("api", context =>
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(key, _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 300,
            QueueLimit = 0
        });
    });

    options.RejectionStatusCode = 429;

    // Default rejection is a bare 429 with no body, which on the login
    // page looks exactly like the form silently doing nothing. Show a
    // visible message instead (fine to be this specific — single-admin
    // app, raw technical errors are OK to surface directly in the UI).
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "text/plain";
        await context.HttpContext.Response.WriteAsync(
            "Too many attempts — rate limit exceeded. Please wait about a minute and try again.", token);
    };
});

builder.Services.AddDistributedMemoryCache();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(@"App_Data/keys"))
    .SetApplicationName("ManFoodsTO");

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(1);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = SessionExtensions.SessionCookieName;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

var connectionString = BuildConnectionString();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString, sql =>
        // Action-plan detection (Services/StoreActionPlanService.cs) issues many
        // sequential per-store queries in a single background job after every
        // monthly upload. The default 30s command timeout was tuned for Neon's
        // low latency and started hitting "Execution Timeout Expired" against
        // MonsterASP's higher round-trip latency — raise it so a single slow
        // command doesn't fail the whole run.
        sql.CommandTimeout(120)));

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IUploadService, UploadService>();
builder.Services.AddSingleton<IBackgroundJobTracker, BackgroundJobTracker>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IStoreService, StoreService>();
builder.Services.AddScoped<IStoreAccessService, StoreAccessService>();
builder.Services.AddScoped<IExitInterviewService, ExitInterviewService>();
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<INinetyDayTurnoverService, NinetyDayTurnoverService>();
builder.Services.AddScoped<IRetentionService, RetentionService>();
builder.Services.AddScoped<IEarlyWarningService, EarlyWarningService>();
builder.Services.AddScoped<IScorecardService, ScorecardService>();
builder.Services.AddScoped<IStoreActionPlanService, StoreActionPlanService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IColorRulesService, ColorRulesService>();
builder.Services.AddScoped<IRecommendationTemplateService, RecommendationTemplateService>();

var app = builder.Build();
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor,
    KnownNetworks = { new IPNetwork(System.Net.IPAddress.Parse("127.0.0.1"), 8),
                      new IPNetwork(System.Net.IPAddress.Parse("10.0.0.0"), 8),
                      new IPNetwork(System.Net.IPAddress.Parse("172.16.0.0"), 12),
                      new IPNetwork(System.Net.IPAddress.Parse("100.64.0.0"), 10) }
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

var supportedCultures = new[] { new CultureInfo("en"), new CultureInfo("ar") };

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("en"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures,
    RequestCultureProviders = new List<IRequestCultureProvider>
    {
        new MfLangCookieProvider()
    }
});

app.Use(async (context, next) =>
{
    var h = context.Response.Headers;

    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "SAMEORIGIN";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["X-Permitted-Cross-Domain-Policies"] = "none";

    h["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com; " +
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com https://cdn.jsdelivr.net; " +
        "img-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'self';";

    h.Remove("X-Powered-By");

    await next();
});

app.UseStaticFiles();

app.UseRouting();

app.UseRateLimiter();

app.UseSession();

app.UseAuthentication();

app.UseAuthorization();


app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Account}/{action=Login}/{id?}");


app.MapGet("/", ctx =>
{
    ctx.Response.Redirect("/login");
    return Task.CompletedTask;
});

app.MapGet("/admin", ctx =>
{
    ctx.Response.Redirect("/adminlogin");
    return Task.CompletedTask;
});

app.MapGet("/home", ctx =>
{
    ctx.Response.Redirect("/login");
    return Task.CompletedTask;
});


app.MapControllerRoute(
    name: "language",
    pattern: "language/{action}/{id?}",
    defaults: new { controller = "Language" });


app.MapControllerRoute(
    name: "api",
    pattern: "api/{controller}/{action}/{id?}");


app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");


using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database initialization failed");
        if (!app.Environment.IsDevelopment())
            throw; // Fail fast in production — a broken DB must not serve traffic
    }
}

app.Run();


// Resolution order mirrors the previous Neon/Postgres setup's fallback chain, adapted
// for SQL Server: a full connection string first (what MonsterASP hands you directly
// from its control panel), then discrete parts assembled safely via
// SqlConnectionStringBuilder (handles escaping of special characters in the password),
// then a local-dev-only fallback so `dotnet run` still works with no secrets configured.
// No credentials are hardcoded anywhere below except that local/dev fallback, which uses
// SQL Server's Trusted_Connection (no username/password at all) rather than a literal
// credential.
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
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
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

    // A generic DATABASE_URL, if set, is treated as an already-complete
    // connection string (unlike the old Postgres setup, there's no widely-used
    // URI scheme for SQL Server connection strings to parse).
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (!string.IsNullOrEmpty(databaseUrl))
        return databaseUrl;

    // Local-dev-only fallback: SQL Server LocalDB with a trusted (Windows-integrated)
    // connection, so no credential is ever hardcoded here.
    return @"Server=(localdb)\MSSQLLocalDB;Database=manfoods;Trusted_Connection=True;TrustServerCertificate=True;";
}
