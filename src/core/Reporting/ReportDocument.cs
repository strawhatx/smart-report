using SmartReport.Core.Policy;
using SmartReport.Core.Sql;

namespace SmartReport.Core.Reporting;

public sealed record KpiTile(string Key, string Label, decimal? Value, string Unit);

public sealed record SeriesPoint(string Label, decimal? Value);

/// <param name="Chart">The form actually drawn, after the request was checked against the data.</param>
/// <param name="ChartNote">Set only when the requested form was replaced, saying what and why.</param>
public sealed record ChartSeries(
    string Key, string Label, string Unit, bool IsTemporal, bool IsPartToWhole,
    ChartKind Chart, string? ChartNote,
    IReadOnlyList<SeriesPoint> Points);

public sealed record ReportTable(
    IReadOnlyList<ResultColumn> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows);

/// <summary>
/// What was read and what was held back.
///
/// <para>
/// Shown under every report. A report that quietly omits what it couldn't show is worse than no
/// report, because the reader has no way to know the number is partial.
/// </para>
/// </summary>
public sealed record Provenance(
    IReadOnlyList<string> TablesRead,
    IReadOnlyList<string> Withheld,
    int RowCount,
    bool RowLimitReached,
    string PlannerProvider);

public sealed record ReportSection(
    string Model,
    string? Label,
    string Sql,
    IReadOnlyList<KpiTile> Kpis,
    IReadOnlyList<ChartSeries> Charts,
    ReportTable Table);

public sealed record ReportDocument(
    string Title,
    IReadOnlyList<ReportSection> Sections,
    IReadOnlyList<PolicyDecision> Decisions,
    Provenance Provenance);
