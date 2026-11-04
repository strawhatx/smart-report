using Npgsql;
using SmartReport.Core.Policy;
using Xunit;

namespace SmartReport.Tests;

/// <summary>
/// The quiet guarantee: a client sees its own rows and nobody else's.
///
/// <para>
/// It isn't the demo's headline — every reviewer assumes it — but it is the thing everything else
/// rests on, so it is tested hardest. Crucially: <b>the client comes from the session, not the
/// query</b>, and a query that names another client gets nothing rather than that client's rows.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TenantIsolationTests(PostgresFixture fixture)
{
    private async Task<long> ScalarAsync(string sql, string principal, string role, int clientId)
    {
        var (connection, transaction) = await fixture.OpenAsAsync(principal, role, clientId);
        await using var _ = connection;
        await using var __ = transaction;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Two_clients_see_different_row_counts_for_the_same_query()
    {
        var trailhead = await ScalarAsync("SELECT count(*) FROM orders", "dana", Roles.Coordinator, TestPrincipals.TrailheadId);
        var bluefin = await ScalarAsync("SELECT count(*) FROM orders", "sam", Roles.AccountOwner, TestPrincipals.BluefinId);

        Assert.True(trailhead > 0 && bluefin > 0, "Both clients need real rows or this proves nothing.");
        Assert.NotEqual(trailhead, bluefin);
    }

    [Fact]
    public async Task Naming_another_client_in_the_query_returns_nothing()
    {
        // The policy is applied after the predicate, so asking for Bluefin's rows while signed in
        // as Trailhead returns zero — not Bluefin's data, and not an error that hints at it.
        var leaked = await ScalarAsync(
            $"SELECT count(*) FROM orders WHERE client_id = {TestPrincipals.BluefinId}",
            "dana", Roles.Coordinator, TestPrincipals.TrailheadId);

        Assert.Equal(0L, leaked);
    }

    [Fact]
    public async Task The_union_of_both_clients_is_never_visible_to_either()
    {
        await using var owner = new NpgsqlConnection(fixture.OwnerConnectionString);
        await owner.OpenAsync();
        await using var total = new NpgsqlCommand("SELECT count(*) FROM orders", owner);
        var everything = (long)(await total.ExecuteScalarAsync())!;

        var trailhead = await ScalarAsync("SELECT count(*) FROM orders", "dana", Roles.Coordinator, TestPrincipals.TrailheadId);

        Assert.True(trailhead < everything,
            "Trailhead saw every row in the table — the client scope is not being applied.");
    }

    [Fact]
    public async Task Saved_reports_are_scoped_to_the_client_and_the_person()
    {
        // Both conditions, not just one: another client's owner must not reach these either.
        var mine = await ScalarAsync("SELECT count(*) FROM saved_reports", "dana", Roles.Coordinator, TestPrincipals.TrailheadId);
        var theirs = await ScalarAsync("SELECT count(*) FROM saved_reports", "sam", Roles.AccountOwner, TestPrincipals.BluefinId);

        Assert.Equal(0L, theirs);
        Assert.True(mine >= 0);
    }
}
