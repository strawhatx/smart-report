using Npgsql;
using SmartReport.Core.Policy;
using Xunit;

namespace SmartReport.Tests;

/// <summary>
/// Connections are pooled and reused. If the principal were applied with a session-level
/// <c>SET</c> instead of a transaction-local one, the next request to pick up that connection
/// would inherit the previous caller's identity — a cross-user data leak that no amount of
/// application code above it could catch.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PrincipalLeakTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_connection_returned_to_the_pool_carries_no_principal()
    {
        // Use and release a connection as a regional manager.
        var (first, transaction) = await fixture.OpenAsAsync("marcus", Roles.RegionalManager, 1);
        await using (first)
        await using (transaction)
        {
            await using var check = new NpgsqlCommand("SELECT count(*) FROM orders", first, transaction);
            Assert.True((long)(await check.ExecuteScalarAsync())! > 0);
            await transaction.RollbackAsync();
        }

        // Take a connection again — very likely the same physical one — and set nothing.
        await using var second = await fixture.AppDatabase.OpenAsync(CancellationToken.None);
        await using var settings = new NpgsqlCommand(
            """
            SELECT coalesce(current_setting('app.role', true), ''),
                   coalesce(current_setting('app.region_id', true), ''),
                   coalesce(current_setting('app.principal', true), '')
            """, second);

        await using var reader = await settings.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        Assert.Equal(string.Empty, reader.GetString(0));
        Assert.Equal(string.Empty, reader.GetString(1));
        Assert.Equal(string.Empty, reader.GetString(2));
    }

    [Fact]
    public async Task A_recycled_connection_sees_no_rows_until_a_principal_is_applied()
    {
        var (used, transaction) = await fixture.OpenAsAsync("priya", Roles.HrDirector, null);
        await using (used) await using (transaction) { await transaction.RollbackAsync(); }

        await using var fresh = await fixture.AppDatabase.OpenAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM compensation", fresh);

        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }
}
