using System.Globalization;
using SmartReport.Core.Abstractions;
using SmartReport.Core.Policy;
using SmartReport.Core.Sql;

namespace SmartReport.Core.Reporting;

/// <summary>
/// Assembles executed queries into a document the UI can render. Pure: no I/O, no formatting
/// decisions that belong to the view (currency symbols, locales) — only shape.
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

        foreach (var (query, result) in executed)
        {
            sections.Add(BuildSection(query, result));
            totalRows += result.Rows.Count;

            var limit = governed.Queries
                .FirstOrDefault(q => q.Query.Model == query.Model)?.RowLimit;
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
        var metrics = query.Metrics;

        // No grouping means one row of totals, which reads as headline figures rather than a chart.
        var kpis = dimensions.Count == 0
            ? BuildKpis(query, result)
            : [];

        var charts = dimensions.Count > 0
            ? BuildCharts(query, result)
            : [];

        return new ReportSection(query.Model, query.Label, query.Sql, kpis, charts, BuildTable(query, result));
    }

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

            series.Add(new ChartSeries(column.Key, column.Label, column.Unit, temporal, partToWhole, points));
        }

        return series;
    }

    private static ReportTable BuildTable(CompiledQuery query, QueryResult result) =>
        new(query.Columns,
            result.Rows.Select(row => (IReadOnlyList<string?>)row.Select(AsText).ToArray()).ToArray());

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
