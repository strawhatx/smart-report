using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartReport.Core.Abstractions;
using SmartReport.Core.Policy;
using SmartReport.Core.Reporting;

namespace SmartReport.Ai;

/// <summary>
/// Asks the model to describe a finished report.
///
/// <para>
/// This is the step that looks like intelligence rather than a lookup: given the actual figures,
/// the model writes "UPS Ground accounts for roughly half of your late shipments" instead of the
/// chart repeating its own title.
/// </para>
/// <para>
/// <b>It is also the only place the model sees data.</b> What it gets is the aggregated report —
/// KPI values and chart points the asker was already authorized for, capped and flattened. Never
/// raw rows, never a column the policy engine withheld, and never the ability to ask for more:
/// by the time this runs the queries are finished.
/// </para>
/// </summary>
public sealed class ChatReportNarrator(
    IChatClient chat,
    IOptions<AiOptions> options,
    ILogger<ChatReportNarrator> logger) : IReportNarrator
{
    private readonly AiOptions _options = options.Value;

    /// <summary>Enough for the model to see the shape without posting the whole table.</summary>
    private const int MaxPointsPerSeries = 24;

    public bool IsEnabled => _options.SummariseResults && !_options.IsScripted;

    public string Disclosure => _options.IsHosted
        ? $"Summary written by {_options.Model} via {_options.Provider}. The figures above were "
          + "sent to that provider; your underlying rows were not."
        : $"Summary written by {_options.Model}, running on this machine. Nothing left the network.";

    public async Task<IReadOnlyList<Finding>> DescribeAsync(
        string question,
        ReportDocument document,
        Principal principal,
        CancellationToken cancellationToken)
    {
        var response = await chat.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, SystemPrompt),
                new ChatMessage(ChatRole.User, Describe(question, document, principal)),
            ],
            new ChatOptions
            {
                Temperature = 0.2f,
                ResponseFormat = ChatResponseFormat.ForJsonSchema(
                    FindingsSchema,
                    schemaName: "report_findings",
                    schemaDescription: "Short factual observations about a finished report."),
            },
            cancellationToken);

        try
        {
            using var json = JsonDocument.Parse(ExtractObject(response.Text));
            if (!json.RootElement.TryGetProperty("findings", out var items))
            {
                return [];
            }

            return items.EnumerateArray()
                .Select(item => new Finding(
                    item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    item.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : ""))
                .Where(f => f.Title.Length > 0 && f.Detail.Length > 0)
                .Take(5)
                .ToArray();
        }
        catch (JsonException ex)
        {
            logger.LogInformation(ex, "Narrator returned unparseable JSON; skipping the summary.");
            return [];
        }
    }

    private const string SystemPrompt = """
        You are summarising a report that has already been produced for one client of a
        fulfilment company. You are shown only its aggregated results.

        Write three to five short findings. Each has a title of a few words and one or two
        sentences of detail.

        Rules:
        - Use only the figures given. Never estimate, extrapolate, or introduce a number that is
          not in the data. If something is not shown, do not mention it.
        - Say what the numbers mean, not what the charts are called. "UPS Ground is about half of
          your late shipments" is useful; "this chart shows late shipments by carrier" is not.
        - Name the figure you are drawing on, so the reader can check you.
        - Plain business English. No preamble, no markdown, no bullet characters.
        - Where a comparison is present, say whether it improved or worsened.
        """;

    /// <summary>
    /// Strict-mode schemas require every property listed in <c>required</c> and
    /// <c>additionalProperties: false</c> on each object, so both are set here.
    /// </summary>
    private static JsonElement FindingsSchema => JsonSerializer.Deserialize<JsonElement>(
        """
        {
          "type": "object",
          "properties": {
            "findings": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "title": { "type": "string" },
                  "detail": { "type": "string" }
                },
                "required": ["title", "detail"],
                "additionalProperties": false
              }
            }
          },
          "required": ["findings"],
          "additionalProperties": false
        }
        """);

    /// <summary>
    /// Flattens the report into text. Deliberately a projection rather than a serialization of
    /// the document: the narrator gets labels and numbers, not the SQL, not the table rows, and
    /// nothing about how the data was reached.
    /// </summary>
    private static string Describe(string question, ReportDocument document, Principal principal)
    {
        var sb = new StringBuilder();
        sb.Append("Client: ").AppendLine(principal.ClientName);
        sb.Append("Question: ").AppendLine(question);
        sb.Append("Report: ").AppendLine(document.Title);
        sb.AppendLine();

        foreach (var section in document.Sections)
        {
            if (section.Label is not null)
            {
                sb.Append('[').Append(section.Label).AppendLine("]");
            }

            foreach (var kpi in section.Kpis)
            {
                sb.Append("- ").Append(kpi.Label).Append(": ")
                  .Append(Format(kpi.Value, kpi.Unit)).AppendLine();
            }

            foreach (var chart in section.Charts)
            {
                sb.Append("- ").Append(chart.Label).AppendLine(" by group:");
                foreach (var point in chart.Points.Take(MaxPointsPerSeries))
                {
                    sb.Append("    ").Append(point.Label).Append(": ")
                      .Append(Format(point.Value, chart.Unit)).AppendLine();
                }
            }

            sb.AppendLine();
        }

        if (document.Provenance.Withheld.Count > 0)
        {
            sb.Append("Withheld from this reader (do not speculate about these): ")
              .AppendLine(string.Join(", ", document.Provenance.Withheld));
        }

        return sb.ToString();
    }

    private static string Format(decimal? value, string unit) => value is null
        ? "no data"
        : unit switch
        {
            "percent" => value.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%",
            "days" => value.Value.ToString("0.0", CultureInfo.InvariantCulture) + " days",
            "currency" => "$" + value.Value.ToString("0.00", CultureInfo.InvariantCulture),
            _ => value.Value.ToString("0.##", CultureInfo.InvariantCulture),
        };

    private static string ExtractObject(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw[start..(end + 1)] : raw;
    }
}
