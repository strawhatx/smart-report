using Npgsql;
using SmartReport.Core.Policy;
using Xunit;

namespace SmartReport.Tests;

/// <summary>
/// Row-level security, tested against a real PostgreSQL with the real policies.
///
/// <para>
/// These assert the guarantee the product rests on: the database, not the application, decides
/// which rows a principal may see. Every query here is a bare <c>SELECT count(*)</c> with no
/// <c>WHERE</c> clause of its own — whatever filtering happens is the policy's doing.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RlsTests(PostgresFixture fixture)
{
    private async Task<long> CountAsync(string table, string? role, int? regionId, string principal = "dana")
    {
        var (connection, transaction) = await fixture.OpenAsAsync(principal, role, regionId);
        await using var _ = connection;
        await using var __ = transaction;

        // The table name is a constant from this test, never user input.
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection, transaction);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task An_unconfigured_connection_sees_nothing()
    {
        // The failure mode that matters most. current_setting(…, true) returns NULL when unset,
        // so a policy that forgot to check for NULL would return every row here.
        Assert.Equal(0, await CountAsync("orders", role: null, regionId: null));
        Assert.Equal(0, await CountAsync("compensation", role: null, regionId: null));
        Assert.Equal(0, await CountAsync("attrition_events", role: null, regionId: null));
    }

    [Fact]
    public async Task An_analyst_sees_every_region()
    {
        var all = await CountAsync("orders", Roles.Analyst, regionId: null);
        Assert.True(all > 0, "The seed should have produced orders.");
    }

    [Fact]
    public async Task A_regional_manager_sees_strictly_fewer_rows_than_an_analyst()
    {
        var all = await CountAsync("orders", Roles.Analyst, regionId: null);
        var scoped = await CountAsync("orders", Roles.RegionalManager, regionId: 1, principal: "marcus");

        Assert.True(scoped > 0, "The North region should have orders.");
        Assert.True(scoped < all, $"Expected scoping to reduce {all} rows; got {scoped}.");
    }

    [Fact]
    public async Task A_regional_manager_without_a_region_sees_nothing_rather_than_everything()
    {
        Assert.Equal(0, await CountAsync("orders", Roles.RegionalManager, regionId: null, principal: "marcus"));
    }

    [Fact]
    public async Task Line_items_are_scoped_the_same_way_as_their_orders()
    {
        var all = await CountAsync("order_items", Roles.Analyst, regionId: null);
        var scoped = await CountAsync("order_items", Roles.RegionalManager, regionId: 1, principal: "marcus");

        Assert.True(scoped < all);
    }

    [Theory]
    [InlineData(Roles.Analyst, null)]
    [InlineData(Roles.RegionalManager, 1)]
    public async Task Compensation_is_invisible_to_everyone_but_hr(string role, int? regionId)
    {
        Assert.Equal(0, await CountAsync("compensation", role, regionId));
    }

    [Fact]
    public async Task Compensation_is_visible_to_hr()
    {
        Assert.True(await CountAsync("compensation", Roles.HrDirector, regionId: null, principal: "priya") > 0);
    }

    [Fact]
    public async Task Saved_reports_are_owner_only()
    {
        var owned = await CountAsync("saved_reports", Roles.Analyst, null, principal: "dana");
        var total = await TotalAsOwnerAsync("saved_reports");

        Assert.True(owned > 0, "Dana should have saved reports.");
        Assert.True(owned < total, $"Dana saw all {total} rows — the owner-only policy is not applying.");
    }

    [Fact]
    public async Task A_cross_region_query_written_by_hand_still_returns_nothing()
    {
        // Even a deliberately hostile query cannot reach another region's rows, because the
        // policy is applied by the database after the predicate, not before it.
        var (connection, transaction) = await fixture.OpenAsAsync("marcus", Roles.RegionalManager, 1);
        await using var _ = connection;
        await using var __ = transaction;

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM orders WHERE region_id <> 1", connection, transaction);

        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task The_application_role_cannot_write()
    {
        var (connection, transaction) = await fixture.OpenAsAsync("dana", Roles.Analyst, null);
        await using var _ = connection;
        await using var __ = transaction;

        await using var command = new NpgsqlCommand("DELETE FROM orders", connection, transaction);

        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal("42501", error.SqlState); // insufficient_privilege
    }

    private async Task<long> TotalAsOwnerAsync(string table)
    {
        await using var owner = new NpgsqlConnection(fixture.OwnerConnectionString);
        await owner.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", owner);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
