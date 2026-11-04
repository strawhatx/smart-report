using SmartReport.Core.Reporting;
using Xunit;

namespace SmartReport.Tests;

/// <summary>
/// A requested chart shape is honoured where the data supports it and replaced where it would
/// mislead — and a replacement is never silent.
/// </summary>
public class ChartChoiceTests
{
    [Fact]
    public void Auto_draws_a_line_for_time()
    {
        var choice = ChartChoice.Resolve(ChartKind.Auto, isTemporal: true, isPartToWhole: false, points: 12);

        Assert.Equal(ChartKind.Line, choice.Kind);
        Assert.Null(choice.Note);
    }

    [Fact]
    public void Auto_draws_a_ring_for_a_small_part_to_whole_split()
    {
        var choice = ChartChoice.Resolve(ChartKind.Auto, isTemporal: false, isPartToWhole: true, points: 4);

        Assert.Equal(ChartKind.Donut, choice.Kind);
    }

    [Fact]
    public void Auto_falls_back_to_bars_for_anything_else()
    {
        Assert.Equal(ChartKind.Bar,
            ChartChoice.Resolve(ChartKind.Auto, isTemporal: false, isPartToWhole: false, points: 5).Kind);
    }

    [Fact]
    public void A_requested_line_over_time_is_honoured()
    {
        var choice = ChartChoice.Resolve(ChartKind.Line, isTemporal: true, isPartToWhole: false, points: 10);

        Assert.Equal(ChartKind.Line, choice.Kind);
        Assert.Null(choice.Note);
    }

    [Fact]
    public void A_requested_line_over_categories_becomes_bars_and_says_why()
    {
        // Carriers have no order. A line between them invents a trend.
        var choice = ChartChoice.Resolve(ChartKind.Line, isTemporal: false, isPartToWhole: false, points: 5);

        Assert.Equal(ChartKind.Bar, choice.Kind);
        Assert.Contains("no order", choice.Note!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_ring_with_too_many_slices_becomes_bars_and_says_why()
    {
        var choice = ChartChoice.Resolve(ChartKind.Donut, isTemporal: false, isPartToWhole: true, points: 12);

        Assert.Equal(ChartKind.Bar, choice.Kind);
        Assert.Contains("12", choice.Note!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ring_within_the_limit_is_honoured()
    {
        Assert.Equal(ChartKind.Donut,
            ChartChoice.Resolve(ChartKind.Donut, isTemporal: false, isPartToWhole: false, points: 6).Kind);
    }

    [Fact]
    public void Bars_are_always_allowed()
    {
        Assert.Equal(ChartKind.Bar,
            ChartChoice.Resolve(ChartKind.Bar, isTemporal: true, isPartToWhole: true, points: 20).Kind);
    }

    [Fact]
    public void Table_is_always_allowed_and_an_empty_result_draws_nothing()
    {
        Assert.Equal(ChartKind.Table,
            ChartChoice.Resolve(ChartKind.Table, isTemporal: true, isPartToWhole: false, points: 9).Kind);
        Assert.Equal(ChartKind.Table,
            ChartChoice.Resolve(ChartKind.Line, isTemporal: true, isPartToWhole: false, points: 0).Kind);
    }
}

/// <summary>
/// Two groupings at once is the case that produced an unreadable chart in practice: the model
/// asked for late shipments by carrier *and* month, and the bars came out with every carrier
/// repeated per period.
/// </summary>
public class MultiDimensionChartTests
{
    [Fact]
    public void Two_groupings_are_shown_as_figures_rather_than_a_flattened_chart()
    {
        var choice = ChartChoice.Resolve(
            ChartKind.Auto, isTemporal: true, isPartToWhole: false, points: 60, dimensions: 2);

        Assert.Equal(ChartKind.Table, choice.Kind);
        Assert.Contains("two things at once", choice.Note!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_requested_chart_does_not_override_that()
    {
        // The reader asking for a line does not make a two-dimensional result line-shaped.
        Assert.Equal(ChartKind.Table,
            ChartChoice.Resolve(ChartKind.Line, isTemporal: true, isPartToWhole: false,
                points: 60, dimensions: 2).Kind);
    }

    [Fact]
    public void One_grouping_is_unaffected()
    {
        Assert.Equal(ChartKind.Bar,
            ChartChoice.Resolve(ChartKind.Auto, isTemporal: false, isPartToWhole: false,
                points: 5, dimensions: 1).Kind);
    }
}
