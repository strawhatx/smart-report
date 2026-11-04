using SmartReport.Core.Policy;
using SmartReport.Core.Specs;
using SmartReport.Core.Sql;
using Xunit;

namespace SmartReport.Tests;

public class SqlCompilerTests
{
    private readonly SqlCompiler _compiler = new(TestPrincipals.Catalog);
    private readonly SpecValidator _validator = new(TestPrincipals.Catalog);
    private readonly PolicyEngine _policy = new(TestPrincipals.Catalog);

    private CompiledQuery Compile(SpecQuery query, Principal? principal = null)
    {
        var validated = _validator.Validate(new ReportSpec("T", [query]));
        Assert.True(validated.IsValid, validated.Summary);

        var governed = _policy.Govern(validated.Spec!, principal ?? TestPrincipals.Priya);
        Assert.False(governed.IsRefused, governed.RefusalReason);

        return _compiler.Compile(governed.Queries[0]);
    }

    [Fact]
    public void Never_emits_select_star()
    {
        // Asterisks appear legitimately as multiplication inside aggregates such as
        // SUM(oi.quantity * oi.unit_price), so this looks for a wildcard column list (D7)
        // rather than for the character.
        foreach (var query in new[]
        {
            new SpecQuery("shipping", ["orders_shipped"], ["carrier"], [], null),
            new SpecQuery("returns", ["refund_value"], ["reason"], [], null),
            new SpecQuery("inventory", ["units_on_hand"], ["product"], [], null),
        })
        {
            var sql = Compile(query).Sql;

            Assert.DoesNotMatch(@"SELECT\s+\*", sql);
            Assert.DoesNotMatch(@"\w\.\*", sql);
        }
    }

    [Fact]
    public void Binds_filter_values_as_parameters_instead_of_writing_them_into_the_sql()
    {
        var compiled = Compile(new SpecQuery("shipping", ["orders_shipped"], ["carrier"],
            [new SpecFilter("carrier", FilterOp.Eq, ["North"])], null));

        Assert.DoesNotContain("North", compiled.Sql, StringComparison.Ordinal);
        Assert.Contains("@p0", compiled.Sql, StringComparison.Ordinal);
        Assert.Equal("North", Assert.Single(compiled.Parameters).Value);
    }

    [Fact]
    public void A_hostile_filter_value_stays_a_value()
    {
        // The validator lets this through because it is a legal piece of *text*. It must then
        // reach the database as data, not as SQL.
        const string hostile = "North'; DROP TABLE orders; --";

        var compiled = Compile(new SpecQuery("shipping", ["orders_shipped"], ["carrier"],
            [new SpecFilter("carrier", FilterOp.Eq, [hostile])], null));

        Assert.DoesNotContain("DROP TABLE", compiled.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(hostile, Assert.Single(compiled.Parameters).Value);
    }

    [Fact]
    public void An_in_filter_binds_one_array_parameter_not_many_fragments()
    {
        var compiled = Compile(new SpecQuery("shipping", ["orders_shipped"], ["carrier"],
            [new SpecFilter("carrier", FilterOp.In, ["North", "South", "East"])], null));

        Assert.Contains("= ANY(@p0)", compiled.Sql, StringComparison.Ordinal);
        Assert.Single(compiled.Parameters);
        Assert.Equal(new[] { "North", "South", "East" }, Assert.IsType<string[]>(compiled.Parameters[0].Value));
    }

    [Fact]
    public void A_date_range_is_half_open_so_a_quarter_does_not_swallow_the_next_one()
    {
        var compiled = Compile(new SpecQuery("shipping", ["orders_shipped"], ["carrier"],
            [new SpecFilter("shipped_at", FilterOp.Between, ["2025-07-01", "2025-10-01"])], null));

        Assert.Contains(">= @p0", compiled.Sql, StringComparison.Ordinal);
        Assert.Contains("< @p1", compiled.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("<= @p1", compiled.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Like_wildcards_in_a_user_value_are_escaped()
    {
        var compiled = Compile(new SpecQuery("shipping", ["orders_shipped"], ["service"],
            [new SpecFilter("channel", FilterOp.Contains, ["100%_pure"])], null));

        // The user's % and _ must match literally, not act as wildcards.
        Assert.Equal(@"%100\%\_pure%", compiled.Parameters[0].Value);
        Assert.Contains(@"ESCAPE '\'", compiled.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Groups_by_ordinal_position()
    {
        var sql = Compile(new SpecQuery("shipping", ["orders_shipped"], ["carrier", "channel"], [], null)).Sql;

        Assert.Contains("GROUP BY 1, 2", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Orders_a_time_series_chronologically()
    {
        var sql = Compile(new SpecQuery("shipping", ["orders_shipped"], ["month"], [], null)).Sql;

        Assert.Contains("ORDER BY 1 ASC", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Orders_a_categorical_breakdown_largest_first()
    {
        var sql = Compile(new SpecQuery("shipping", ["orders_shipped"], ["carrier"], [], null)).Sql;

        Assert.Contains("ORDER BY 2 DESC NULLS LAST", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Always_applies_a_limit()
    {
        Assert.Contains("LIMIT ", Compile(new SpecQuery("shipping", ["orders_shipped"], [], [], null)).Sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Describes_its_result_columns_for_rendering()
    {
        var compiled = Compile(new SpecQuery("shipping", ["orders_shipped", "late_shipments"], ["carrier"], [], null));

        Assert.Equal(["carrier", "orders_shipped", "late_shipments"], compiled.Columns.Select(c => c.Key));
        Assert.Single(compiled.Dimensions);
        Assert.Equal(2, compiled.Metrics.Count);
        Assert.Equal("number", compiled.Columns.Single(c => c.Key == "orders_shipped").Unit);
    }

    [Fact]
    public void Reports_the_tables_it_read_for_provenance()
    {
        var compiled = Compile(new SpecQuery("shipping", ["orders_shipped"], ["carrier"], [], null));

        Assert.Contains("shipments", compiled.Tables);
        Assert.Contains("orders", compiled.Tables);
    }
}
