namespace SmartReport.Core.Catalog;

/// <summary>Declared type of a dimension or filter field, used to parse and bind filter values.</summary>
public enum FieldType
{
    Text,
    Number,
    Date,
}

/// <summary>
/// A predicate the compiler adds to <b>every</b> query against a model, whatever was asked for.
///
/// <para>
/// This is how the minimum-cohort floor on benchmarks is kept: it is not a filter the planner
/// chooses, and not a check the policy engine might skip — it is welded into the SQL by
/// <see cref="Sql.SqlCompiler"/> for anyone, in any query, every time.
/// </para>
/// </summary>
/// <param name="ExpressionFormat">
/// SQL with a single <c>{0}</c> where the bound parameter goes, e.g.
/// <c>bm.client_count &gt;= {0}</c>. The value is always a parameter, never interpolated.
/// </param>
/// <param name="Value">The bound value.</param>
/// <param name="Reason">Shown to the user, so the safeguard is stated rather than silent.</param>
public sealed record MandatoryPredicate(string ExpressionFormat, object Value, string Reason);

/// <summary>
/// A measure the user can ask for.
/// </summary>
/// <param name="Expression">
/// A SQL aggregate fragment. <b>Authored here, in source, and the only way SQL text enters the
/// system.</b> It never contains anything from a user or from the model.
/// </param>
/// <param name="RequiredRoles">Roles permitted to use this metric. <c>null</c> means any role.</param>
/// <param name="RestrictionNote">
/// Why it is restricted, in the user's terms. Beats a generic denial — "Carrier contract rates are
/// Northpoint's internal data" tells someone what kind of thing they hit.
/// </param>
/// <param name="Alternative">
/// A <c>model.metric</c> key to offer instead. <see cref="Policy.PolicyEngine"/> only suggests it
/// after checking the asker may actually use it — a suggestion that leads to a second refusal is
/// worse than none.
/// </param>
public sealed record MetricDefinition(
    string Key,
    string Label,
    string Expression,
    string Unit = "number",
    IReadOnlyList<string>? RequiredRoles = null,
    string? RestrictionNote = null,
    string? Alternative = null)
{
    public bool IsAllowedFor(string role) => RequiredRoles is null || RequiredRoles.Contains(role);
}

/// <summary>A field the user can group by. <paramref name="Expression"/> is source-authored, as above.</summary>
/// <param name="IsTemporal">
/// True for time buckets. A result grouped by one of these is ordered chronologically rather than
/// by size, because a time series read largest-first is nonsense.
/// </param>
/// <param name="IsPartToWhole">
/// True when the groups are a complete, mutually exclusive split of one total that a reader takes
/// in at a glance — every order has exactly one sales channel. Those are drawn as a single ring
/// with the total in the middle; anything the reader needs to <i>compare</i> or rank is drawn as
/// bars, which sit on a shared baseline and are far easier to read exactly.
/// </param>
public sealed record DimensionDefinition(
    string Key,
    string Label,
    string Expression,
    FieldType Type = FieldType.Text,
    IReadOnlyList<string>? RequiredRoles = null,
    bool IsTemporal = false,
    bool IsPartToWhole = false)
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
    IReadOnlyList<string> Tables,
    IReadOnlyList<MandatoryPredicate>? MandatoryPredicates = null,
    IReadOnlyList<string>? RequiredRoles = null,
    string? RestrictionNote = null)
{
    public IReadOnlyList<MandatoryPredicate> Safeguards => MandatoryPredicates ?? [];

    public bool IsAllowedFor(string role) => RequiredRoles is null || RequiredRoles.Contains(role);

    public MetricDefinition? Metric(string key) => Metrics.GetValueOrDefault(key);
    public DimensionDefinition? Dimension(string key) => Dimensions.GetValueOrDefault(key);
    public FilterFieldDefinition? Filter(string key) => Filters.GetValueOrDefault(key);
}
