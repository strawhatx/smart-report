using System.Globalization;
using SmartReport.Core.Catalog;

namespace SmartReport.Core.Specs;

/// <summary>
/// Checks a planner's output against the catalog before anything downstream touches it.
///
/// <para>
/// This is the boundary where model output stops being trusted. Every key must resolve to a
/// catalog entry, every operator must suit the field's type, and every value must parse as that
/// type. A spec that fails here is never compiled, so a hostile string such as
/// <c>"metric": "'; DROP TABLE orders; --"</c> dies as an unknown metric key — it is never
/// anything the compiler could emit.
/// </para>
/// </summary>
public sealed class SpecValidator(DataCatalog catalog)
{
    private static readonly FilterOp[] TextOps = [FilterOp.Eq, FilterOp.In, FilterOp.Contains];
    private static readonly FilterOp[] NumberOps = [FilterOp.Eq, FilterOp.In, FilterOp.Gte, FilterOp.Lte, FilterOp.Between];
    private static readonly FilterOp[] DateOps = [FilterOp.Gte, FilterOp.Lte, FilterOp.Between, FilterOp.Eq];

    public SpecValidationResult Validate(ReportSpec? spec)
    {
        var problems = new List<SpecProblem>();

        if (spec is null || spec.Queries.Count == 0)
        {
            problems.Add(new SpecProblem(SpecProblemKind.Empty, "queries",
                "The plan contained no queries."));
            return SpecValidationResult.Invalid(problems);
        }

        if (spec.Queries.Count > SpecLimits.MaxQueries)
        {
            problems.Add(new SpecProblem(SpecProblemKind.OutOfBounds, "queries",
                $"A report may combine at most {SpecLimits.MaxQueries} queries."));
            return SpecValidationResult.Invalid(problems);
        }

        var title = string.IsNullOrWhiteSpace(spec.Title)
            ? "Report"
            : spec.Title.Trim();
        if (title.Length > SpecLimits.MaxTitleLength)
        {
            title = title[..SpecLimits.MaxTitleLength];
        }

        var normalizedQueries = new List<SpecQuery>(spec.Queries.Count);
        for (var i = 0; i < spec.Queries.Count; i++)
        {
            var normalized = ValidateQuery(spec.Queries[i], $"queries[{i}]", problems);
            if (normalized is not null)
            {
                normalizedQueries.Add(normalized);
            }
        }

        return problems.Count > 0
            ? SpecValidationResult.Invalid(problems)
            : SpecValidationResult.Valid(new ReportSpec(title, normalizedQueries));
    }

    private SpecQuery? ValidateQuery(SpecQuery query, string path, List<SpecProblem> problems)
    {
        var model = catalog.Model(query.Model);
        if (model is null)
        {
            problems.Add(new SpecProblem(SpecProblemKind.UnknownModel, $"{path}.model",
                $"There is no data model called '{Describe(query.Model)}'."));
            return null;
        }

        var metrics = ValidateKeys(
            query.Metrics, model.Metrics.Keys, $"{path}.metrics",
            SpecProblemKind.UnknownMetric, "measure", model.Key, problems);

        if (metrics.Count == 0)
        {
            problems.Add(new SpecProblem(SpecProblemKind.Empty, $"{path}.metrics",
                "A query needs at least one measure."));
        }
        else if (metrics.Count > SpecLimits.MaxMetricsPerQuery)
        {
            problems.Add(new SpecProblem(SpecProblemKind.OutOfBounds, $"{path}.metrics",
                $"At most {SpecLimits.MaxMetricsPerQuery} measures per query."));
        }

        var dimensions = ValidateKeys(
            query.Dimensions, model.Dimensions.Keys, $"{path}.dimensions",
            SpecProblemKind.UnknownDimension, "grouping", model.Key, problems);

        if (dimensions.Count > SpecLimits.MaxDimensionsPerQuery)
        {
            problems.Add(new SpecProblem(SpecProblemKind.OutOfBounds, $"{path}.dimensions",
                $"At most {SpecLimits.MaxDimensionsPerQuery} groupings per query."));
        }

        if (query.Filters.Count > SpecLimits.MaxFiltersPerQuery)
        {
            problems.Add(new SpecProblem(SpecProblemKind.OutOfBounds, $"{path}.filters",
                $"At most {SpecLimits.MaxFiltersPerQuery} filters per query."));
        }

        var filters = new List<SpecFilter>(query.Filters.Count);
        for (var i = 0; i < query.Filters.Count; i++)
        {
            var filter = ValidateFilter(query.Filters[i], model, $"{path}.filters[{i}]", problems);
            if (filter is not null)
            {
                filters.Add(filter);
            }
        }

        var limit = query.Limit is null
            ? SpecLimits.DefaultRowLimit
            : Math.Clamp(query.Limit.Value, 1, SpecLimits.MaxRowLimit);

        return new SpecQuery(model.Key, metrics, dimensions, filters, limit);
    }

    private static List<string> ValidateKeys(
        IReadOnlyList<string> requested,
        IEnumerable<string> allowed,
        string path,
        SpecProblemKind kind,
        string noun,
        string modelKey,
        List<SpecProblem> problems)
    {
        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        var accepted = new List<string>(requested.Count);

        for (var i = 0; i < requested.Count; i++)
        {
            var key = requested[i];
            if (!allowedSet.Contains(key))
            {
                problems.Add(new SpecProblem(kind, $"{path}[{i}]",
                    $"'{Describe(key)}' is not a {noun} available on the {modelKey} model."));
                continue;
            }

            // Duplicates are dropped rather than rejected: harmless, and a planner repeating
            // itself shouldn't cost the user their answer.
            if (!accepted.Contains(key, StringComparer.Ordinal))
            {
                accepted.Add(key);
            }
        }

        return accepted;
    }

    private static SpecFilter? ValidateFilter(
        SpecFilter filter, QueryModel model, string path, List<SpecProblem> problems)
    {
        var field = model.Filter(filter.Field);
        if (field is null)
        {
            problems.Add(new SpecProblem(SpecProblemKind.UnknownFilterField, $"{path}.field",
                $"'{Describe(filter.Field)}' is not a filterable field on the {model.Key} model."));
            return null;
        }

        var allowedOps = field.Type switch
        {
            FieldType.Text => TextOps,
            FieldType.Number => NumberOps,
            FieldType.Date => DateOps,
            _ => [],
        };

        if (!allowedOps.Contains(filter.Op))
        {
            problems.Add(new SpecProblem(SpecProblemKind.BadOperator, $"{path}.op",
                $"'{filter.Op}' cannot be used on {field.Key}, which is a {field.Type.ToString().ToLowerInvariant()} field."));
            return null;
        }

        var expectedValues = filter.Op switch
        {
            FilterOp.Between => 2,
            FilterOp.In => -1,
            _ => 1,
        };

        if (expectedValues == -1)
        {
            if (filter.Values.Count is 0 or > SpecLimits.MaxFilterValues)
            {
                problems.Add(new SpecProblem(SpecProblemKind.BadValue, $"{path}.values",
                    $"An 'in' filter needs between 1 and {SpecLimits.MaxFilterValues} values."));
                return null;
            }
        }
        else if (filter.Values.Count != expectedValues)
        {
            problems.Add(new SpecProblem(SpecProblemKind.BadValue, $"{path}.values",
                $"'{filter.Op}' on {field.Key} needs exactly {expectedValues} value(s)."));
            return null;
        }

        foreach (var value in filter.Values)
        {
            if (!ValueParses(value, field.Type))
            {
                problems.Add(new SpecProblem(SpecProblemKind.BadValue, $"{path}.values",
                    $"'{Describe(value)}' is not a valid {field.Type.ToString().ToLowerInvariant()} value for {field.Key}."));
                return null;
            }
        }

        return new SpecFilter(field.Key, filter.Op, filter.Values);
    }

    private static bool ValueParses(string value, FieldType type) => type switch
    {
        FieldType.Number => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
        FieldType.Date => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _),
        FieldType.Text => value.Length is > 0 and <= SpecLimits.MaxTextValueLength,
        _ => false,
    };

    /// <summary>
    /// Trims and flattens an unrecognised key before it appears in a message. The value is
    /// already HTML-encoded by Razor on the way out; this keeps a long or multi-line planner
    /// string from turning a validation message into a wall of text.
    /// </summary>
    private static string Describe(string raw)
    {
        var cleaned = raw.ReplaceLineEndings(" ").Trim();
        return cleaned.Length <= 40 ? cleaned : cleaned[..40] + "…";
    }
}
