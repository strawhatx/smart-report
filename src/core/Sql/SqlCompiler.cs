using System.Globalization;
using System.Text;
using SmartReport.Core.Catalog;
using SmartReport.Core.Policy;
using SmartReport.Core.Specs;

namespace SmartReport.Core.Sql;

/// <summary>
/// Turns an authorized query into parameterized SQL.
///
/// <para>
/// <b>Every identifier emitted here was read out of <see cref="DataCatalog"/>.</b> Nothing from
/// the question, and nothing from the planner, is ever written into the SQL text — planner output
/// only ever selects <i>which</i> catalog entry to use, and user values are bound as parameters.
/// That is the product's central claim, and this class is where it is kept.
/// </para>
/// </summary>
public sealed class SqlCompiler(DataCatalog catalog)
{
    public CompiledQuery Compile(GovernedQuery governed)
    {
        var query = governed.Query;
        var model = catalog.Model(query.Model)
            ?? throw new InvalidOperationException($"Unknown model '{query.Model}' reached the compiler.");

        var parameters = new List<QueryParameter>();
        var columns = new List<ResultColumn>();
        var sql = new StringBuilder();

        sql.AppendLine("SELECT");

        var selectParts = new List<string>();
        foreach (var key in query.Dimensions)
        {
            var dimension = model.Dimension(key)!;
            selectParts.Add($"  {dimension.Expression} AS {Quote(dimension.Key)}");
            columns.Add(new ResultColumn(dimension.Key, dimension.Label, IsMetric: false, "text",
                dimension.IsTemporal, dimension.IsPartToWhole));
        }

        foreach (var key in query.Metrics)
        {
            var metric = model.Metric(key)!;
            selectParts.Add($"  {metric.Expression} AS {Quote(metric.Key)}");
            columns.Add(new ResultColumn(metric.Key, metric.Label, IsMetric: true, metric.Unit, IsTemporal: false));
        }

        sql.AppendLine(string.Join(",\n", selectParts));
        sql.Append("FROM ").AppendLine(model.From);

        var predicates = new List<string>();

        // Safeguards first, and unconditionally. These are not filters the planner chose and not
        // checks the policy engine might have skipped — they are welded into every query against
        // the model, for every principal. The minimum-cohort floor on benchmarks is one of these:
        // a cohort of three clients returns nothing rather than three clients' averages in a
        // disguise. The value is bound, never interpolated.
        foreach (var safeguard in model.Safeguards)
        {
            predicates.Add(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                safeguard.ExpressionFormat,
                Bind(parameters, safeguard.Value)));
        }

        foreach (var filter in query.Filters)
        {
            predicates.Add(BuildPredicate(model.Filter(filter.Field)!, filter, parameters));
        }

        if (predicates.Count > 0)
        {
            sql.Append("WHERE ").AppendLine(string.Join("\n  AND ", predicates));
        }

        if (query.Dimensions.Count > 0)
        {
            // Ordinal GROUP BY keeps the expression in one place; Postgres resolves it to the
            // select list, so a change to a catalog expression can't drift out of sync here.
            sql.Append("GROUP BY ")
               .AppendLine(string.Join(", ", Enumerable.Range(1, query.Dimensions.Count)));

            sql.Append("ORDER BY ").AppendLine(BuildOrderBy(query, model));
        }

        sql.Append("LIMIT ").Append(governed.RowLimit.ToString(CultureInfo.InvariantCulture)).Append(';');

        return new CompiledQuery(model.Key, query.Label, sql.ToString(), parameters, columns, model.Tables);
    }

    /// <summary>
    /// A time series reads chronologically; anything else reads largest-first. The ordinal refers
    /// to the select list, so no expression is repeated.
    /// </summary>
    private static string BuildOrderBy(SpecQuery query, QueryModel model)
    {
        for (var i = 0; i < query.Dimensions.Count; i++)
        {
            if (model.Dimension(query.Dimensions[i])!.IsTemporal)
            {
                return $"{i + 1} ASC";
            }
        }

        return query.Metrics.Count > 0
            ? $"{query.Dimensions.Count + 1} DESC NULLS LAST"
            : "1 ASC";
    }

    private static string BuildPredicate(
        FilterFieldDefinition field, SpecFilter filter, List<QueryParameter> parameters)
    {
        var expression = field.Expression;

        switch (filter.Op)
        {
            case FilterOp.Eq:
                return $"{expression} = {Bind(parameters, Convert(filter.Values[0], field.Type))}";

            case FilterOp.In:
                // = ANY(array) takes a single parameter, so an 'in' with twenty values is still
                // one bound value rather than twenty pieces of generated SQL.
                var values = filter.Values.Select(v => Convert(v, field.Type)).ToArray();
                return $"{expression} = ANY({Bind(parameters, ToArray(values, field.Type))})";

            case FilterOp.Gte:
                return $"{expression} >= {Bind(parameters, Convert(filter.Values[0], field.Type))}";

            case FilterOp.Lte:
                return $"{expression} <= {Bind(parameters, Convert(filter.Values[0], field.Type))}";

            case FilterOp.Between when field.Type == FieldType.Date:
                // Half-open for dates: "Q3" must not swallow the first instant of Q4.
                return $"({expression} >= {Bind(parameters, Convert(filter.Values[0], field.Type))} "
                     + $"AND {expression} < {Bind(parameters, Convert(filter.Values[1], field.Type))})";

            case FilterOp.Between:
                return $"({expression} BETWEEN {Bind(parameters, Convert(filter.Values[0], field.Type))} "
                     + $"AND {Bind(parameters, Convert(filter.Values[1], field.Type))})";

            case FilterOp.Contains:
                // The value is escaped for LIKE and then bound. The wildcards are ours, not the
                // user's: a question containing '%' searches for a literal percent sign.
                var pattern = "%" + EscapeLike((string)Convert(filter.Values[0], field.Type)) + "%";
                return $"{expression} ILIKE {Bind(parameters, pattern)} ESCAPE '\\'";

            default:
                throw new InvalidOperationException(
                    $"Operator '{filter.Op}' reached the compiler for field '{field.Key}'.");
        }
    }

    private static string Bind(List<QueryParameter> parameters, object value)
    {
        var name = $"p{parameters.Count}";
        parameters.Add(new QueryParameter(name, value));
        return "@" + name;
    }

    private static object Convert(string raw, FieldType type) => type switch
    {
        FieldType.Number => decimal.Parse(raw, NumberStyles.Number, CultureInfo.InvariantCulture),
        FieldType.Date => DateTime.Parse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
        _ => raw,
    };

    private static object ToArray(object[] values, FieldType type) => type switch
    {
        FieldType.Number => values.Cast<decimal>().ToArray(),
        FieldType.Date => values.Cast<DateTime>().ToArray(),
        _ => values.Cast<string>().ToArray(),
    };

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>
    /// Quotes a result alias.
    ///
    /// <para>
    /// Catalog keys are source-authored, so this can only fail if someone adds a malformed key —
    /// which is exactly when a loud build-time failure is wanted rather than silently emitted SQL.
    /// </para>
    /// </summary>
    private static string Quote(string identifier)
    {
        foreach (var c in identifier)
        {
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '_')
            {
                throw new InvalidOperationException(
                    $"Catalog key '{identifier}' is not a safe identifier (expected lowercase, digits and underscores).");
            }
        }

        return '"' + identifier + '"';
    }
}
