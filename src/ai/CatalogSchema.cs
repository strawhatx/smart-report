using System.Text.Json;
using System.Text.Json.Nodes;
using SmartReport.Core.Catalog;

namespace SmartReport.Ai;

/// <summary>
/// Builds a JSON Schema for a report plan <b>from the catalog</b>.
///
/// <para>
/// This is the reason to use a provider abstraction that supports structured output rather than
/// asking politely for JSON. Metric, dimension and filter fields are declared as <c>enum</c>s of
/// the keys that actually exist, so an invented measure is <i>structurally impossible</i> — the
/// model cannot emit one, rather than emitting one that gets rejected afterwards.
/// </para>
/// <para>
/// <see cref="Core.Specs.SpecValidator"/> still runs on the result. Schema enforcement varies by
/// provider and is not a thing to depend on alone — but between the two, an off-catalog key never
/// reaches the compiler.
/// </para>
/// </summary>
public static class CatalogSchema
{
    public static JsonElement Build(DataCatalog catalog)
    {
        var models = catalog.Models.ToArray();

        // A union of keys across models rather than a per-model oneOf: providers vary in how well
        // they handle conditional subschemas, and the validator catches a key used on the wrong
        // model anyway. Inventing a key outright is what this closes off.
        var metricKeys = Distinct(models.SelectMany(m => m.Metrics.Keys));
        var dimensionKeys = Distinct(models.SelectMany(m => m.Dimensions.Keys));
        var filterKeys = Distinct(models.SelectMany(m => m.Filters.Keys));

        var filterSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["field"] = Enum(filterKeys),
                ["op"] = Enum(["eq", "in", "gte", "lte", "between", "contains"]),
                ["values"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" },
                },
            },
            ["required"] = Array("field", "op", "values"),
            ["additionalProperties"] = false,
        };

        var querySchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["model"] = Enum(models.Select(m => m.Key).ToArray()),
                ["metrics"] = new JsonObject { ["type"] = "array", ["items"] = Enum(metricKeys) },
                ["dimensions"] = new JsonObject { ["type"] = "array", ["items"] = Enum(dimensionKeys) },
                ["filters"] = new JsonObject { ["type"] = "array", ["items"] = filterSchema },
                ["label"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] =
                        "\"headline\" for summary figures, \"comparison:<Period>\" for the "
                        + "previous period, or \"\" for an ordinary panel.",
                },
                ["chart"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = Array("auto", "bar", "line", "donut", "table"),
                    ["description"] =
                        "How to draw it. Use auto unless the reader asked for something "
                        + "specific. line needs a time grouping; donut needs six parts or fewer.",
                },
            },
            ["required"] = Array("model", "metrics", "dimensions", "filters", "label", "chart"),
            ["additionalProperties"] = false,
        };

        var root = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["title"] = new JsonObject { ["type"] = "string" },
                ["queries"] = new JsonObject { ["type"] = "array", ["items"] = querySchema },
                ["no_match"] = new JsonObject { ["type"] = "boolean" },
                ["message"] = new JsonObject { ["type"] = "string" },
            },
            ["required"] = Array("title", "queries", "no_match", "message"),
            ["additionalProperties"] = false,
        };

        return JsonSerializer.Deserialize<JsonElement>(root.ToJsonString());
    }

    private static string[] Distinct(IEnumerable<string> keys) =>
        keys.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToArray();

    private static JsonObject Enum(IReadOnlyList<string> values) =>
        new() { ["type"] = "string", ["enum"] = Array([.. values]) };

    private static JsonArray Array(params string[] values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }
        return array;
    }
}
