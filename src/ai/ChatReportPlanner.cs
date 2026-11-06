using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartReport.Core.Abstractions;
using SmartReport.Core.Catalog;
using SmartReport.Core.Policy;

namespace SmartReport.Ai;

/// <summary>
/// Plans with whatever model is configured — Claude, GPT, or one on your own hardware.
///
/// <para>
/// The model is given the catalog's keys and a JSON Schema generated from them. It is given no
/// schema, no connection, and <b>no rows</b>: what leaves this machine is the question and a list
/// of metric names, never a customer's data. That is a consequence of planning a spec rather than
/// writing SQL, and it is what makes a hosted model defensible here at all.
/// </para>
/// <para>
/// If the provider fails, this throws. It deliberately does not fall back to the scripted
/// planner: quietly answering from a canned table while claiming a model answered would be the
/// one genuinely dishonest thing this app could do.
/// </para>
/// </summary>
public sealed class ChatReportPlanner(
    IChatClient chat,
    DataCatalog catalog,
    IOptions<AiOptions> options,
    ILogger<ChatReportPlanner> logger) : IReportPlanner
{
    private readonly AiOptions _options = options.Value;

    /// <summary>A model can act on the validation errors; a lookup table cannot.</summary>
    public bool CanRepair => true;

    public string ProviderName => _options.Provider.ToLowerInvariant() switch
    {
        "ollama" or "openai-compatible" => $"{_options.Model} (local)",
        _ => _options.Model,
    };

    public async IAsyncEnumerable<PlannerEvent> PlanAsync(
        PlanRequest request,
        Principal principal,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new PlannerEvent.Thinking(
            request.IsRetry ? $"Asking {ProviderName} again, with the catalog errors."
            : request.IsFollowUp ? $"Asking {ProviderName} to change the report you're looking at."
            : $"Asking {ProviderName} to pick measures from the catalog.");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, BuildPrompt(request, principal)),
        };

        string content;
        var degraded = false;

        try
        {
            content = await CompleteAsync(messages, SchemaFormat(), cancellationToken);
        }
        catch (Exception ex) when (IsSchemaRejection(ex))
        {
            // Not every model on every provider accepts a JSON schema, and which ones do has
            // moved before. Dropping to plain JSON mode keeps the demo working — but it weakens
            // the guarantee from "an invented key is impossible" to "an invented key is
            // rejected", so it is said out loud rather than absorbed.
            logger.LogWarning(ex,
                "{Model} on {Provider} rejected the JSON schema; falling back to JSON object mode.",
                _options.Model, _options.Provider);

            degraded = true;
            content = await CompleteAsync(messages, ChatResponseFormat.Json, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Logged in full, reported narrowly (C5).
            logger.LogError(ex, "Planner provider {Provider} failed.", _options.Provider);
            throw new InvalidOperationException(
                $"The model provider ({_options.Provider}) did not answer. Check Ai:ApiKey and "
                + "Ai:BaseUrl, or set Ai:Provider to 'scripted'.", ex);
        }

        if (degraded)
        {
            yield return new PlannerEvent.Thinking(
                $"{_options.Model} does not accept a JSON schema, so the plan is only validated "
                + "after the fact, not constrained during generation.");
        }

        yield return new PlannerEvent.Thinking("Reading the plan it returned.");

        var parsed = PlannerJson.Parse(content);

        if (parsed.NoMatch || parsed.Spec is null)
        {
            yield return new PlannerEvent.NoMatch(
                parsed.Message ?? "The model couldn't match that question to the catalog.");
            yield break;
        }

        yield return new PlannerEvent.Planned(parsed.Spec);
    }

    private ChatResponseFormat SchemaFormat() => ChatResponseFormat.ForJsonSchema(
        CatalogSchema.Build(catalog),
        schemaName: "report_plan",
        schemaDescription: "A report plan expressed only in catalog keys.");

    private async Task<string> CompleteAsync(
        IList<ChatMessage> messages, ChatResponseFormat format, CancellationToken cancellationToken)
    {
        var response = await chat.GetResponseAsync(
            messages,
            new ChatOptions { Temperature = 0, ResponseFormat = format },
            cancellationToken);

        return response.Text;
    }

    /// <summary>
    /// Providers signal "this model cannot do schemas" as an ordinary 400, so the message is the
    /// only thing to go on. Matched narrowly: a genuine outage must not be mistaken for this.
    /// </summary>
    private static bool IsSchemaRejection(Exception ex) =>
        ex is not OperationCanceledException
        && ex.Message.Contains("json_schema", StringComparison.OrdinalIgnoreCase)
        && (ex.Message.Contains("not support", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("unsupported", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("invalid", StringComparison.OrdinalIgnoreCase));

    private const string SystemPrompt = """
        You translate a question about an order-fulfilment account into a JSON report plan.

        You do not write SQL and you never see the data. You may only use the model, metric,
        dimension and filter keys in the catalog you are given. If the question needs something
        that is not there, set no_match to true and say what is missing.

        Build a COMPLETE report, not the single chart that was literally asked for.

        Someone asking "returns by reason this quarter" wants to understand returns. The reason
        split is where they want to start, not the whole of what they want. Give them the picture
        and let them narrow it afterwards — they can always say "just the reasons".

        Every report, unless told otherwise:
        1. A "headline" query: two to four key measures with NO dimensions, so it opens with
           figures rather than a chart. Label it "headline".
        2. A "comparison:<PeriodName>" query — the same measures over the period before, labelled
           for example "comparison:September". It becomes the change shown against the headline,
           not a chart of its own.
           CRITICAL: only include a comparison if the headline query is itself filtered to a
           period, and the two periods are the same length. Comparing one month against all of
           history produces a meaningless percentage. If the reader named no period, either scope
           the headline to the most recent month and compare with the one before, or leave the
           comparison out entirely.
        3. The breakdown they asked for, first.
        4. Two or three more breakdowns from the same model that a reader would want next — the
           trend over time, and the other dimensions that model offers. Prefer a time trend and a
           part-to-whole split.
        5. Where another model adds obvious context, one query from it. Returns are easier to
           judge beside order volume; shipping performance beside the peer benchmark.

        Aim for four to seven queries. Go narrow only when the message says so — "just",
        "only", "nothing else" — or when it is a follow-up changing a report that already exists.

        Follow-ups:
        - You may be shown the plan behind the report the reader is already looking at. If their
          message changes it rather than starting over — "make that a bar chart", "and by
          channel", "now just Shopify", "drop the comparison" — return the previous plan with
          that change applied and everything else untouched.
        - Change only what they asked to change. Keep the same models, measures, groupings,
          filters and labels otherwise.
        - If the message is plainly a new question, ignore the previous plan.

        Charts:
        - Leave "chart" as "auto" unless the reader asked for a particular shape. Auto picks a
          line for time, a ring for a small part-to-whole split, and bars otherwise.
        - "bar" is horizontal, "column" is vertical. Either is fine for categories.
        - "line" and "area" only when the grouping is time. Across categories they imply an order
          that is not there, and the server will replace them with bars and say so.
        - "donut" only for six parts or fewer that add up to a whole.
        - "table" when the reader wants the figures rather than a picture.

        Rules:
        - Dates are ISO 8601. A "between" filter takes exactly two values: start inclusive, end
          exclusive.
        - Use more than one query only when the question genuinely spans more than one model.
        - Omit dimensions entirely for a headline total.
        - You do not decide who may see what. Ask for what the question needs; the server decides
          whether the person asking is allowed it, and explains the refusal.
        - Always set every field. Use no_match false and message "" for a normal plan.
        """;

    private string BuildPrompt(PlanRequest request, Principal principal)
    {
        var prompt = new System.Text.StringBuilder();
        prompt.AppendLine("Catalog:").AppendLine(catalog.DescribeForPlanner());
        prompt.Append("Today is ").Append($"{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}")
              .AppendLine(". The data ends 2026-10-31.");
        prompt.Append("The question comes from ").Append(principal.JobTitle)
              .Append(" at ").Append(principal.ClientName).AppendLine(".");

        if (request.IsFollowUp)
        {
            prompt.AppendLine()
                  .AppendLine("The reader is currently looking at this report.")
                  .Append("Their previous message was: ").AppendLine(request.PreviousQuestion ?? "(unknown)")
                  .AppendLine("The plan behind it was:")
                  .AppendLine(Truncate(request.PreviousPlanJson!, 4000));
        }

        if (request.IsRetry)
        {
            prompt.AppendLine()
                  .AppendLine("Your previous attempt was rejected:")
                  .AppendLine(Truncate(request.ValidationProblems!, 1000))
                  .AppendLine("Fix exactly that and keep the rest. A filter only exists on the "
                              + "model it is listed under — 'period' works on every model that "
                              + "has a date.");
        }

        prompt.AppendLine().Append("Message: ").Append(request.Question);
        return prompt.ToString();
    }

    /// <summary>
    /// Caps the context. A plan is small, but it arrives from the browser and nothing should be
    /// able to push an unbounded string into a billed request.
    /// </summary>
    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

}
