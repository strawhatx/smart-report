using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartReport.Data;

/// <summary>
/// Used only by <c>dotnet ef</c>.
///
/// <para>
/// Migrations connect as the database <b>owner</b>, which is the one place that is correct —
/// creating tables and policies requires it. Request handling never uses this connection (D4).
/// The string comes from the environment so no credential is committed (C2).
/// </para>
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SmartReportDbContext>
{
    public const string ConnectionVariable = "SMARTREPORT_OWNER_CONNECTION";

    public SmartReportDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionVariable} before running EF tools, for example:\n" +
                $"  export {ConnectionVariable}=\"Host=localhost;Database=smartreport;Username=smartreport;Password=…\"\n" +
                "Values come from your .env (see .env.example); nothing is defaulted here on purpose.");
        }

        var options = new DbContextOptionsBuilder<SmartReportDbContext>()
            .UseNpgsql(connection)
            .Options;

        return new SmartReportDbContext(options);
    }
}
