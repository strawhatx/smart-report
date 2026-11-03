namespace SmartReport.Core.Catalog;

/// <summary>Declared type of a dimension or filter field, used to parse and bind filter values.</summary>
public enum FieldType
{
    Text,
    Number,
    Date,
}

/// <summary>
/// A measure the user can ask for.
/// </summary>
/// <param name="Expression">
/// A SQL aggregate fragment. <b>This is authored here, in source, and is the only way SQL text
/// enters the system.</b> It never contains anything supplied by a user or by the model — see
/// <see cref="Sql.SqlCompiler"/>. Filter values always travel as parameters.
/// </param>
/// <param name="RequiredRoles">Roles permitted to use this metric. <c>null</c> means any role.</param>
public sealed record MetricDefinition(
    string Key,
    string Label,
    string Expression,
    string Unit = "number",
    IReadOnlyList<string>? RequiredRoles = null)
{
    public bool IsAllowedFor(string role) => RequiredRoles is null || RequiredRoles.Contains(role);
}

/// <summary>A field the user can group by. <paramref name="Expression"/> is source-authored, as above.</summary>
/// <param name="IsTemporal">
/// True for time buckets such as month or quarter. A result grouped by one of these is ordered
/// chronologically rather than by size, because a time series read largest-first is nonsense.
/// </param>
public sealed record DimensionDefinition(
    string Key,
    string Label,
    string Expression,
    FieldType Type = FieldType.Text,
    IReadOnlyList<string>? RequiredRoles = null,
    bool IsTemporal = false)
{
    public bool IsAllowedFor(string role) => RequiredRoles is null || RequiredRoles.Contains(role);
}

/// <summary>A field the user can filter on. <paramref name="Expression"/> is source-authored, as above.</summary>
public sealed record FilterFieldDefinition(
    string Key,
    string Label,
    string Expression,
    FieldType Type,
    IReadOnlyList<string>? RequiredRoles = null)
{
    public bool IsAllowedFor(string role) => RequiredRoles is null || RequiredRoles.Contains(role);
}

/// <summary>
/// One star schema: a fixed FROM clause plus the metrics, dimensions and filters available over it.
/// A question can only ever be answered inside a model that already exists here.
/// </summary>
public sealed record QueryModel(
    string Key,
    string Label,
    string Description,
    string From,
    IReadOnlyDictionary<string, MetricDefinition> Metrics,
    IReadOnlyDictionary<string, DimensionDefinition> Dimensions,
    IReadOnlyDictionary<string, FilterFieldDefinition> Filters,
    IReadOnlyList<string> Tables)
{
    public MetricDefinition? Metric(string key) => Metrics.GetValueOrDefault(key);
    public DimensionDefinition? Dimension(string key) => Dimensions.GetValueOrDefault(key);
    public FilterFieldDefinition? Filter(string key) => Filters.GetValueOrDefault(key);
}
