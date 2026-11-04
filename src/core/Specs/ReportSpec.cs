namespace SmartReport.Core.Specs;

/// <summary>Comparison allowed in a filter. Anything outside this set is rejected.</summary>
public enum FilterOp
{
    Eq,
    In,
    Gte,
    Lte,
    Between,
    Contains,
}

/// <summary>
/// One filter. <see cref="Values"/> are always bound as query parameters — never interpolated —
/// so they are data even when they contain SQL-looking text.
/// </summary>
public sealed record SpecFilter(string Field, FilterOp Op, IReadOnlyList<string> Values);

/// <summary>One aggregate query against a single <see cref="Catalog.QueryModel"/>.</summary>
/// <param name="Label">
/// What this panel is, when the model key alone would not say. Two queries against
/// <c>shipping</c> can be "this month" and "last month"; the UI uses the label to pair them into
/// a delta instead of drawing the same chart twice.
/// </param>
public sealed record SpecQuery(
    string Model,
    IReadOnlyList<string> Metrics,
    IReadOnlyList<string> Dimensions,
    IReadOnlyList<SpecFilter> Filters,
    int? Limit = null,
    string? Label = null);

/// <summary>
/// What the planner returns: a structured request, not code. Several queries are allowed so one
/// report can draw on more than one model (for example turnover cost beside programme budget).
/// </summary>
public sealed record ReportSpec(string Title, IReadOnlyList<SpecQuery> Queries);

/// <summary>Hard bounds applied to every spec, whichever planner produced it.</summary>
public static class SpecLimits
{
    public const int MaxQueries = 6;
    public const int MaxMetricsPerQuery = 6;
    public const int MaxDimensionsPerQuery = 3;
    public const int MaxFiltersPerQuery = 8;
    public const int MaxFilterValues = 20;
    public const int MaxTextValueLength = 200;
    public const int MaxTitleLength = 120;

    public const int DefaultRowLimit = 500;
    public const int MaxRowLimit = 1000;
}
