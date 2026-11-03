using System.Text.Json;
using System.Text.Json.Serialization;
using SmartReport.Core.Specs;

namespace SmartReport.Ai;

/// <summary>
/// Parses a planner's JSON into a <see cref="ReportSpec"/>.
///
/// <para>
/// Deliberately lenient about <i>shape</i> and completely strict about <i>meaning</i>: a model
/// that returns a slightly odd envelope still gets its intent read, but nothing here checks
/// whether the keys exist. That is <see cref="SpecValidator"/>'s job, and it runs next. Parsing
/// and authorizing are kept apart so neither can be mistaken for the other.
/// </para>
/// </summary>
public static class PlannerJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed class SpecDto
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("queries")] public List<QueryDto>? Queries { get; set; }

        // Some models answer with a single query rather than a list; accept both envelopes.
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("metrics")] public List<string>? Metrics { get; set; }
        [JsonPropertyName("dimensions")] public List<string>? Dimensions { get; set; }
        [JsonPropertyName("filters")] public List<FilterDto>? Filters { get; set; }
        [JsonPropertyName("no_match")] public bool NoMatch { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
    }

    private sealed class QueryDto
    {
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("metrics")] public List<string>? Metrics { get; set; }
        [JsonPropertyName("dimensions")] public List<string>? Dimensions { get; set; }
        [JsonPropertyName("filters")] public List<FilterDto>? Filters { get; set; }
        [JsonPropertyName("limit")] public int? Limit { get; set; }
    }

    private sealed class FilterDto
    {
        [JsonPropertyName("field")] public string? Field { get; set; }
        [JsonPropertyName("op")] public string? Op { get; set; }
        [JsonPropertyName("values")] public List<string>? Values { get; set; }
        [JsonPropertyName("value")] public string? Value { get; set; }
    }

    public sealed record ParseResult(ReportSpec? Spec, bool NoMatch, string? Message);

    public static ParseResult Parse(string json)
    {
        SpecDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<SpecDto>(ExtractJsonObject(json), Options);
        }
        catch (JsonException)
        {
            // Malformed output is an ordinary outcome with a local model, not an exception
            // worth surfacing to the user (C5).
            return new ParseResult(null, false, "The planner returned something that wasn't valid JSON.");
        }

        if (dto is null)
        {
            return new ParseResult(null, false, "The planner returned nothing usable.");
        }

        if (dto.NoMatch)
        {
            return new ParseResult(null, true, dto.Message);
        }

        var queries = new List<SpecQuery>();

        foreach (var query in dto.Queries ?? [])
        {
            queries.Add(ToQuery(query.Model, query.Metrics, query.Dimensions, query.Filters, query.Limit));
        }

        if (queries.Count == 0 && dto.Model is not null)
        {
            queries.Add(ToQuery(dto.Model, dto.Metrics, dto.Dimensions, dto.Filters, null));
        }

        if (queries.Count == 0)
        {
            return new ParseResult(null, true, dto.Message ?? "The planner proposed no queries.");
        }

        return new ParseResult(new ReportSpec(dto.Title ?? "Report", queries), false, dto.Message);
    }

    private static SpecQuery ToQuery(
        string? model, List<string>? metrics, List<string>? dimensions, List<FilterDto>? filters, int? limit) =>
        new(
            // An absent model name becomes an empty string, which the validator rejects as an
            // unknown model. Nothing is guessed on the model's behalf.
            model ?? string.Empty,
            metrics?.Where(m => !string.IsNullOrWhiteSpace(m)).ToArray() ?? [],
            dimensions?.Where(d => !string.IsNullOrWhiteSpace(d)).ToArray() ?? [],
            (filters ?? []).Select(ToFilter).ToArray(),
            limit);

    private static SpecFilter ToFilter(FilterDto filter)
    {
        var values = filter.Values is { Count: > 0 }
            ? filter.Values
            : filter.Value is not null ? [filter.Value] : new List<string>();

        // An unrecognised operator maps to Contains, which the validator then rejects for any
        // field where it isn't allowed. Guessing a *narrower* default would silently change
        // what the user asked for.
        var op = Enum.TryParse<FilterOp>(filter.Op, ignoreCase: true, out var parsed)
            ? parsed
            : FilterOp.Contains;

        return new SpecFilter(filter.Field ?? string.Empty, op, values);
    }

    /// <summary>
    /// Pulls the first JSON object out of a response. Local models commonly wrap their answer in
    /// prose or a fenced code block, and refusing those would be pedantry rather than safety —
    /// the content is validated either way.
    /// </summary>
    private static string ExtractJsonObject(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw[start..(end + 1)] : raw;
    }
}
