using Microsoft.EntityFrameworkCore;
using SmartReport.Data;

namespace SmartReport.App;

/// <summary>
/// Brings a fresh checkout up on first run: migrate, then seed.
///
/// <para>
/// Development only, and on the owner connection, which is the one place that is correct (D4).
/// In any other environment migrations are a deliberate deployment step, not something the web
/// process does to itself on startup (D8).
/// </para>
/// </summary>
public static class DevelopmentStartup
{
    public static async Task PrepareDatabaseAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

        var db = scope.ServiceProvider.GetService<SmartReportDbContext>();
        if (db is null)
        {
            logger.LogWarning(
                "No owner connection configured, so the database was not prepared. "
                + "Copy .env.example to .env if the app can't start.");
            return;
        }

        try
        {
            await db.Database.MigrateAsync();
            await DbSeeder.SeedAsync(db);
            logger.LogInformation("Database ready.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Could not prepare the database. Is Postgres running? "
                + "docker compose -f docker-compose.local.yml up -d");
            throw;
        }
    }
}
