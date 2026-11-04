using Npgsql;
using SmartReport.Core.Catalog;
using SmartReport.Core.Policy;
using SmartReport.Core.Specs;
using SmartReport.Core.Sql;
using Xunit;

namespace SmartReport.Tests;

/// <summary>
/// The headline beat: a client can ask how they compare, and the answer cannot be unpicked into
/// the clients behind it.
///
/// <para>
/// This is where a naive text-to-SQL bot leaks other customers' data, so it gets three
/// independent defences and a test for each: a table with no client column, a cohort floor welded
/// into every query, and the same floor repeated in row-level security.
/// </para>
/// </summary>
public class BenchmarkDisclosureTests
{
    private readonly DataCatalog _catalog = TestPrincipals.Catalog;

    private CompiledQuery Compile(SpecQuery query, Principal principal)
    {
        var validated = new SpecValidator(_catalog).Validate(new ReportSpec("T", [query]));
        Assert.True(validated.IsValid, validated.Summary);

        var governed = new PolicyEngine(_catalog).Govern(validated.Spec!, principal);
        Assert.False(governed.IsRefused, governed.RefusalReason);

        return new SqlCompiler(_catalog).Compile(governed.Queries[0]);
    }

    [Fact]
    public void The_cohort_floor_is_in_every_benchmark_query_even_an_unfiltered_one()
    {
        var compiled = Compile(new SpecQuery("benchmarks", ["peer_median"], [], [], null), TestPrincipals.Dana);

        Assert.Contains("client_count >=", compiled.Sql, StringComparison.Ordinal);
        Assert.Contains(DataCatalog.MinimumCohortSize, compiled.Parameters.Select(p => p.Value));
    }

    [Fact]
    public void The_floor_survives_whatever_else_was_asked_for()
    {
        // A planner that adds its own filters must not be able to displace the safeguard.
        var compiled = Compile(new SpecQuery("benchmarks", ["peer_median", "cohort_size"], ["month"],
            [new SpecFilter("metric", FilterOp.Eq, ["on_time_rate"])], null), TestPrincipals.Dana);

        Assert.Contains("client_count >=", compiled.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void The_benchmark_model_cannot_reference_a_client_at_all()
    {
        var model = _catalog.Model("benchmarks")!;

        // No client_id anywhere in the model: not in the FROM, not as a dimension, not as a filter.
        // There is no join back to anyone's rows, so there is nothing to attribute.
        Assert.DoesNotContain("client_id", model.From, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(model.Dimensions.Values, d => d.Expression.Contains("client_id", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(model.Filters.Values, f => f.Expression.Contains("client_id", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(model.Metrics.Values, m => m.Expression.Contains("client_id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_safeguard_is_stated_to_the_user_rather_than_applied_silently()
    {
        var validated = new SpecValidator(_catalog).Validate(
            new ReportSpec("T", [new SpecQuery("benchmarks", ["peer_median"], [], [], null)]));

        var governed = new PolicyEngine(_catalog).Govern(validated.Spec!, TestPrincipals.Dana);

        var decision = Assert.Single(governed.Decisions, d => d.Outcome == PolicyOutcome.Aggregated);
        Assert.Contains(DataCatalog.MinimumCohortSize.ToString(), decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Everyone_gets_the_floor_including_the_account_owner()
    {
        foreach (var principal in new[] { TestPrincipals.Dana, TestPrincipals.Priya })
        {
            Assert.Contains("client_count >=",
                Compile(new SpecQuery("benchmarks", ["peer_median"], [], [], null), principal).Sql,
                StringComparison.Ordinal);
        }
    }
}

/// <summary>The database half of the same guarantee, against a real PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
public sealed class BenchmarkDisclosureDatabaseTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_thin_cohort_is_unreadable_even_to_a_hand_written_query()
    {
        // Northpoint had seven clients in early 2025. Those rows exist; they are not publishable.
        var (connection, transaction) = await fixture.OpenAsAsync("dana", Roles.Coordinator, TestPrincipals.TrailheadId);
        await using var _ = connection;
        await using var __ = transaction;

        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM benchmark_metrics WHERE client_count < {DataCatalog.MinimumCohortSize}",
            connection, transaction);

        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task The_thin_cohort_rows_really_do_exist_underneath()
    {
        // Otherwise the test above would pass against an empty table and prove nothing.
        await using var owner = new NpgsqlConnection(fixture.OwnerConnectionString);
        await owner.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM benchmark_metrics WHERE client_count < {DataCatalog.MinimumCohortSize}", owner);

        Assert.True((long)(await command.ExecuteScalarAsync())! > 0,
            "Seed some sub-floor benchmark rows, or the suppression test is vacuous.");
    }

    [Fact]
    public async Task A_large_enough_cohort_is_readable()
    {
        var (connection, transaction) = await fixture.OpenAsAsync("dana", Roles.Coordinator, TestPrincipals.TrailheadId);
        await using var _ = connection;
        await using var __ = transaction;

        await using var command = new NpgsqlCommand("SELECT count(*) FROM benchmark_metrics", connection, transaction);

        Assert.True((long)(await command.ExecuteScalarAsync())! > 0);
    }
}
