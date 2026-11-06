using System.Globalization;
using SmartReport.Core.Abstractions;
using SmartReport.Core.Policy;
using SmartReport.Core.Sql;

namespace SmartReport.Core.Reporting;

/// <summary>
/// Assembles executed queries into a document the UI can render.
///
/// <para>
/// Pure: no I/O, and no formatting that belongs to the view. Values stay as <c>decimal</c> and
/// units stay as names (<c>"currency"</c>, <c>"percent"</c>) so the browser decides how a figure
/// reads — formatting here would bake one locale into the API.
/// </para>
/// <para>
/// This is also where a result's <i>shape</i> is decided: a query with no grouping becomes
/// headline tiles, one with a grouping becomes charts. The planner does not choose that, because
/// it depends on what actually came back.
/// </para>
/// </summary>
public static class ReportBuilder
{
    public static ReportDocument Build(
        GovernedSpec governed,
        IReadOnlyList<(CompiledQuery Query, QueryResult Result)> executed,
        string plannerProvider)
    {
        var sections = new List<ReportSection>(executed.Count);
        var totalRows = 0;
        var limitReached = false;

        for (var i = 0; i < executed.Count; i++)
        {
            var (query, result) = executed[i];
            sections.Add(BuildSection(query, result));
            totalRows += result.Rows.Count;

            // Matched by position, not by model name. A full report runs several queries against
            // the same model — shipping by carrier, by channel, by month — so looking the limit
            // up by name would find whichever came first and could compare a result against a
            // different query's cap. The pipeline executes governed.Queries in order, so the
            // index is the reliable link.
            var limit = i < governed.Queries.Count ? governed.Queries[i].RowLimit : (int?)null;
            if (limit is not null && result.Rows.Count >= limit)
            {
                limitReached = true;
            }
        }

        return new ReportDocument(
            governed.Title,
            sections,
            governed.Decisions,
            new Provenance(
                governed.TablesRead,
                governed.Withheld,
                totalRows,
                limitReached,
                plannerProvider));
    }

    private static ReportSection BuildSection(CompiledQuery query, QueryResult result)
    {
        var dimensions = query.Dimensions;

        // No grouping means one row of totals, which reads as headline figures rather than a chart.
        var kpis = dimensions.Count == 0
            ? BuildKpis(query, result)
            : [];

        var charts = dimensions.Count > 0
            ? BuildCharts(query, result)
            : [];

        return new ReportSection(query.Model, query.Label, query.Sql, kpis, charts, BuildTable(query, result));
    }

    /// <summary>
    /// Headline figures from an ungrouped query.
    ///
    /// <para>
    /// An aggregate with no GROUP BY returns exactly one row, so row zero is the whole answer —
    /// and no rows means the filters excluded everything, which is a blank panel rather than a
    /// zero (a zero would claim the count is zero; it isn't known).
    /// </para>
    /// </summary>
    private static IReadOnlyList<KpiTile> BuildKpis(CompiledQuery query, QueryResult result)
    {
        if (result.Rows.Count == 0)
        {
            return [];
        }

        var row = result.Rows[0];
        var tiles = new List<KpiTile>(query.Metrics.Count);

        for (var i = 0; i < query.Columns.Count; i++)
        {
            var column = query.Columns[i];
            if (column.IsMetric)
            {
                tiles.Add(new KpiTile(column.Key, column.Label, AsDecimal(row[i]), column.Unit));
            }
        }

        return tiles;
    }

    /// <summary>
    /// One series per measure, all sharing the grouping as their labels.
    ///
    /// <para>
    /// Three measures grouped by carrier become three charts, not one chart with three colours:
    /// orders, on-time rate and days-to-ship have different scales, and putting them on one axis
    /// would invent a relationship between them.
    /// </para>
    /// <para>
    /// Only the <i>first</i> grouping supplies the labels. A result grouped by two things is
    /// handled by <see cref="ChartChoice"/>, which turns it into a table rather than flattening
    /// one grouping into the other.
    /// </para>
    /// </summary>
    private static IReadOnlyList<ChartSeries> BuildCharts(CompiledQuery query, QueryResult result)
    {
        var labelIndex = query.Columns.ToList().FindIndex(c => !c.IsMetric);
        if (labelIndex < 0)
        {
            return [];
        }

        var series = new List<ChartSeries>();
        var temporal = query.Columns[labelIndex].IsTemporal;
        var partToWhole = query.Columns[labelIndex].IsPartToWhole;

        for (var i = 0; i < query.Columns.Count; i++)
        {
            var column = query.Columns[i];
            if (!column.IsMetric)
            {
                continue;
            }

            var points = result.Rows
                .Select(row => new SeriesPoint(AsText(row[labelIndex]) ?? "—", AsDecimal(row[i])))
                .ToArray();

            var choice = ChartChoice.Resolve(
                query.Chart, temporal, partToWhole, points.Length, query.Dimensions.Count);

            series.Add(new ChartSeries(
                column.Key, column.Label, column.Unit, temporal, partToWhole, column.IsAdditive,
                choice.Kind, choice.Note, points));
        }

        return series;
    }

    /// <summary>
    /// Every column as text, always — so a chart is never the only way to read a figure, and
    /// Export has something to write whatever shape was drawn.
    /// </summary>
    private static ReportTable BuildTable(CompiledQuery query, QueryResult result) =>
        new(query.Columns,
            result.Rows.Select(row => (IReadOnlyList<string?>)row.Select(AsText).ToArray()).ToArray());

    /// <summary>
    /// Npgsql returns whatever Postgres chose — <c>bigint</c> for a count, <c>numeric</c> for a
    /// sum, <c>double</c> for an average — so every numeric shape is funnelled to one type
    /// before it reaches the view. The string fallback is a last resort, not the expected path.
    /// </summary>
    private static decimal? AsDecimal(object? value) => value switch
    {
        null or DBNull => null,
        decimal d => d,
        double dbl => (decimal)dbl,
        float f => (decimal)f,
        long l => l,
        int i => i,
        short s => s,
        _ => decimal.TryParse(
                 value.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
             ? parsed
             : null,
    };

    private static string? AsText(object? value) => value switch
    {
        null or DBNull => null,
        DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        double dbl => dbl.ToString(CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
