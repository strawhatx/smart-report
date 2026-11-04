using Npgsql;
using SmartReport.Core.Policy;
using Xunit;

namespace SmartReport.Tests;

/// <summary>
/// Row-level security against a real PostgreSQL with the real policies.
///
/// <para>
/// Every query here is a bare <c>SELECT count(*)</c> with no <c>WHERE</c> of its own, so whatever
/// filtering happens is the database's doing, not the test's.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RlsTests(PostgresFixture fixture)
{
    private async Task<long> CountAsync(string table, string? role, int? clientId, string principal = "dana")
    {
        var (connection, transaction) = await fixture.OpenAsAsync(principal, role, clientId);
        await using var _ = connection;
        await using var __ = transaction;

        // The table name is a constant from this test, never user input.
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection, transaction);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("shipments")]
    [InlineData("products")]
    [InlineData("fees")]
    [InlineData("benchmark_metrics")]
    [InlineData("report_templates")]
    public async Task An_unconfigured_connection_sees_nothing(string table)
    {
        // The failure that matters most. set_config cannot store NULL, so "unset" arrives as an
        // empty string — a policy checking only IS NOT NULL would wave this straight through.
        Assert.Equal(0, await CountAsync(table, role: null, clientId: null));
    }

    [Fact]
    public async Task A_role_without_a_client_sees_nothing()
    {
        Assert.Equal(0, await CountAsync("orders", Roles.Coordinator, clientId: null));
        Assert.Equal(0, await CountAsync("shipments", Roles.AccountOwner, clientId: null));
    }

    [Fact]
    public async Task A_configured_client_sees_its_own_rows()
    {
        Assert.True(await CountAsync("orders", Roles.Coordinator, TestPrincipals.TrailheadId) > 0);
        Assert.True(await CountAsync("shipments", Roles.Coordinator, TestPrincipals.TrailheadId) > 0);
    }

    [Fact]
    public async Task The_application_role_cannot_write()
    {
        var (connection, transaction) = await fixture.OpenAsAsync("dana", Roles.Coordinator, TestPrincipals.TrailheadId);
        await using var _ = connection;
        await using var __ = transaction;

        await using var command = new NpgsqlCommand("DELETE FROM orders", connection, transaction);

        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal("42501", error.SqlState); // insufficient_privilege
    }

    [Fact]
    public async Task You_can_always_read_your_own_user_row_so_the_app_can_bootstrap()
    {
        // With no role set, client_users must still return exactly one row — otherwise resolving
        // a principal would need an owner connection, which request handling must never use (D4).
        var (connection, transaction) = await fixture.OpenAsAsync("dana", role: null, clientId: null);
        await using var _ = connection;
        await using var __ = transaction;

        await using var command = new NpgsqlCommand("SELECT count(*) FROM client_users", connection, transaction);

        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }
}
