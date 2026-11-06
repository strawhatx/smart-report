using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SmartReport.App;
using SmartReport.Ai;
using SmartReport.App.Endpoints;
using SmartReport.Core.Reporting;
using SmartReport.App.Security;
using SmartReport.Core.Abstractions;
using SmartReport.Core.Catalog;
using SmartReport.Core.Policy;
using SmartReport.Core.Sql;
using SmartReport.Core.Specs;
using SmartReport.Data;

// Both locations, because the working directory differs by how the app was started: the project
// folder under `dotnet run` and F5, the repository root under `dotnet run --project src/app`.
// Existing environment variables always win, so this can never override real configuration.
DotEnv.LoadIfPresent(Directory.GetCurrentDirectory());
DotEnv.LoadIfPresent(Path.Combine(Directory.GetCurrentDirectory(), "..", ".."));

var builder = WebApplication.CreateBuilder(args);

var appConnection = builder.Configuration.GetConnectionString("App")
    ?? DotEnv.BuildConnectionString("APP_DB_USER", "APP_DB_PASSWORD")
    ?? throw new InvalidOperationException(
        "No application database connection. Copy .env.example to .env and fill it in.");

// Owner access exists only for migrations and seeding (D4). Request handling never touches it.
var ownerConnection = builder.Configuration.GetConnectionString("Owner")
    ?? DotEnv.BuildConnectionString("POSTGRES_USER", "POSTGRES_PASSWORD");

builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();

// --- Core pipeline. Catalog is a singleton because it is immutable; the rest are cheap.
// Singletons because every one of these is immutable and holds no per-request state. The
// catalog in particular is read on every question; building it per request would be waste.
builder.Services.AddSingleton(DataCatalog.CreateDefault());
builder.Services.AddSingleton<SpecValidator>();
builder.Services.AddSingleton<PolicyEngine>();
builder.Services.AddSingleton<SqlCompiler>();

// --- Data access as the non-owner application role (A6).
// AppDatabase is a singleton — it holds a connection string, and Npgsql owns the pool behind it.
// Everything that resolves a principal is scoped, so one request's identity can never outlive it.
builder.Services.AddSingleton(new AppDatabase(appConnection));
builder.Services.AddScoped<PrincipalDirectory>();
builder.Services.AddScoped<SavedReportStore>();
builder.Services.AddScoped<IReportQueryRunner, ReportQueryRunner>();
builder.Services.AddScoped<PrincipalContext>();
builder.Services.AddScoped<ReportPipeline>();

if (ownerConnection is not null)
{
    builder.Services.AddDbContext<SmartReportDbContext>(options => options.UseNpgsql(ownerConnection));
}

// --- Planner. Scripted by default so the demo runs with nothing installed; Ollama is a
// configuration change, and it never silently falls back (see OllamaReportPlanner).
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
var aiOptions = builder.Configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();

if (aiOptions.IsScripted)
{
    builder.Services.AddSingleton<IReportPlanner, ScriptedReportPlanner>();
}
else
{
    // One interface, whichever provider is configured. Built once at startup so a missing key
    // fails here rather than on someone's first question.
    builder.Services.AddSingleton(ChatClientFactory.Create(aiOptions));
    builder.Services.AddSingleton<IReportPlanner, ChatReportPlanner>();
    builder.Services.AddSingleton<IReportNarrator, ChatReportNarrator>();
}

// --- Authentication. The cookie carries a principal key and nothing else (A1, A5).
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/";
        options.AccessDeniedPath = "/";
        options.Cookie.Name = "smartreport.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // SameAsRequest rather than Always, so the cookie survives plain HTTP in development.
        // Behind TLS in production the request is already HTTPS, so the cookie is marked Secure.
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

// --- Rate limiting. The ask endpoint is the one that spends inference (C7).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("ask", context => RateLimitPartition.GetFixedWindowLimiter(
        // Per signed-in principal, falling back to IP for anyone who isn't.
        partitionKey: context.User.FindFirstValue(PrincipalContext.PrincipalKeyClaim)
                      ?? context.Connection.RemoteIpAddress?.ToString()
                      ?? "anonymous",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 12,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await DevelopmentStartup.PrepareDatabaseAsync(app);
}
else
{
    // Generic error page, full detail to the log (C5).
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Order matters. Headers go on before anything can short-circuit the pipeline, so a redirect or
// a 404 carries them too; authentication precedes authorization; the rate limiter sits after
// authentication so it can partition by the signed-in principal rather than by IP.
app.UseHttpsRedirection();
app.UseSmartReportSecurityHeaders();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapRazorPages();
app.MapReportStream();
app.MapSessionEndpoints();

app.Run();

/// <summary>Exposed so the test host can reference this assembly.</summary>
public partial class Program;
