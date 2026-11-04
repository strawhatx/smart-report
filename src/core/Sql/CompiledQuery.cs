using SmartReport.Core.Catalog;

namespace SmartReport.Core.Sql;

/// <summary>A value bound to the query. Never concatenated into the SQL text.</summary>
public sealed record QueryParameter(string Name, object Value);

/// <summary>One column of the result set, described for rendering.</summary>
public sealed record ResultColumn(
    string Key, string Label, bool IsMetric, string Unit, bool IsTemporal, bool IsPartToWhole = false);

/// <summary>
/// Ready-to-execute SQL plus its parameters.
///
/// <para>
/// <see cref="Sql"/> is shown to the user in the UI. That is safe, and deliberate: the whole
/// point of the demo is that the query is inspectable and that nothing in it came from the
/// question.
/// </para>
/// </summary>
public sealed record CompiledQuery(
    string Model,
    string? Label,
    string Sql,
    IReadOnlyList<QueryParameter> Parameters,
    IReadOnlyList<ResultColumn> Columns,
    IReadOnlyList<string> Tables)
{
    public IReadOnlyList<ResultColumn> Dimensions => Columns.Where(c => !c.IsMetric).ToArray();
    public IReadOnlyList<ResultColumn> Metrics => Columns.Where(c => c.IsMetric).ToArray();
}
