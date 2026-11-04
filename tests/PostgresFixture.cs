using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartReport.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace SmartReport.Tests;

/// <summary>
/// A real PostgreSQL instance with the real migrations and the real seed.
///
/// <para>
/// Row-level security cannot be meaningfully faked — an in-memory provider has no policies, so a
/// test against one would pass while proving nothing. These tests therefore need Docker, and they
/// fail loudly rather than skipping when it isn't there: a silently skipped security test is
/// worse than no test, because the suite still reports green.
/// </para>
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("smartreport")
        .WithUsername("smartreport")
        .WithPassword("test-only-not-a-secret")
        .Build();

    /// <summary>Owner connection. Migrations and seeding only (D4).</summary>
    public string OwnerConnectionString { get; private set; } = string.Empty;

    /// <summary>Non-owner application connection. Everything else goes through this (A6).</summary>
    public string AppConnectionString { get; private set; } = string.Empty;

    public AppDatabase AppDatabase { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "These tests need Docker. Start Docker Desktop and try again — they are not "
                + "skipped on purpose, because row-level security cannot be verified without a "
                + "real PostgreSQL.", ex);
        }

        OwnerConnectionString = _container.GetConnectionString();

        // The application role must exist before the migration, which grants privileges to it.
        await using (var owner = new NpgsqlConnection(OwnerConnectionString))
        {
            await owner.OpenAsync();
            await using var create = new NpgsqlCommand(
                """
                CREATE ROLE smartreport_app LOGIN PASSWORD 'test-only-not-a-secret'
                  NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
                GRANT USAGE ON SCHEMA public TO smartreport_app;
                """, owner);
            await create.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(OwnerConnectionString)
        {
            Username = "smartreport_app",
            Password = "test-only-not-a-secret",
        };
        AppConnectionString = builder.ConnectionString;
        AppDatabase = new AppDatabase(AppConnectionString);

        await using var db = CreateOwnerContext();
        await db.Database.MigrateAsync();
        await DbSeeder.SeedAsync(db);
    }

    public SmartReportDbContext CreateOwnerContext() =>
        new(new DbContextOptionsBuilder<SmartReportDbContext>()
            .UseNpgsql(OwnerConnectionString).Options);

    /// <summary>Opens a connection as the application role with the given principal applied.</summary>
    public async Task<(NpgsqlConnection Connection, NpgsqlTransaction Transaction)> OpenAsAsync(
        string principalKey, string? role, int? clientId)
    {
        var connection = await AppDatabase.OpenAsync(CancellationToken.None);
        var transaction = await AppDatabase.BeginAsync(connection, CancellationToken.None);
        await AppDatabase.ApplyPrincipalAsync(
            connection, transaction, principalKey, role, clientId, CancellationToken.None);
        return (connection, transaction);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
