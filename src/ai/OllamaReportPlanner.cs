using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartReport.Core.Abstractions;
using SmartReport.Core.Catalog;
using SmartReport.Core.Policy;

namespace SmartReport.Ai;

/// <summary>
/// Plans against a local model over Ollama's HTTP API, or any OpenAI-compatible server.
///
/// <para>
/// The model is told the catalog's keys and nothing else — no schema, no connection, no sample
/// rows. Whatever comes back is parsed and then validated, so the worst a confused or hostile
/// model can do is produce a spec that fails validation.
/// </para>
/// <para>
/// If the server isn't reachable this throws. It deliberately does <b>not</b> fall back to the
/// scripted planner: during a demo, silently answering from a canned table while claiming to run
/// a local model would be the one genuinely dishonest thing this app could do.
/// </para>
/// </summary>
public sealed class OllamaReportPlanner(
    HttpClient http,
    DataCatalog catalog,
    IOptions<AiOptions> options,
    ILogger<OllamaReportPlanner> logger) : IReportPlanner
{
    private readonly AiOptions _options = options.Value;

    public string ProviderName => $"{_options.Model} (local)";

    public async IAsyncEnumerable<PlannerEvent> PlanAsync(
        string question,
        Principal principal,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new PlannerEvent.Thinking($"Asking {_options.Model} on this machine.");

        string content;
        try
        {
            content = await CompleteAsync(question, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Logged in full, reported narrowly (C5). The user is told the model is unreachable,
            // not given the exception.
            logger.LogError(ex, "Local model at {BaseUrl} was unreachable.", _options.BaseUrl);
            throw new InvalidOperationException(
                $"The local model at {_options.BaseUrl} did not respond. Start it, or set Ai:Provider to 'scripted'.",
                ex);
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

    private async Task<string> CompleteAsync(string question, CancellationToken cancellationToken)
    {
        var prompt = BuildPrompt(question);

        if (_options.UseOpenAiCompatibleApi)
        {
            var openAi = await http.PostAsJsonAsync("/v1/chat/completions", new
            {
                model = _options.Model,
                response_format = new { type = "json_object" },
                temperature = 0,
                messages = new[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = prompt },
                },
            }, cancellationToken);

            openAi.EnsureSuccessStatusCode();
            using var openAiJson = JsonDocument.Parse(
                await openAi.Content.ReadAsStringAsync(cancellationToken));

            return openAiJson.RootElement
                .GetProperty("choices")[0].GetProperty("message").GetProperty("content")
                .GetString() ?? string.Empty;
        }

        var response = await http.PostAsJsonAsync("/api/chat", new
        {
            model = _options.Model,
            format = "json",
            stream = false,
            options = new { temperature = 0 },
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = prompt },
            },
        }, cancellationToken);

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return json.RootElement.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }

    private const string SystemPrompt = """
        You translate business questions into a JSON report specification.

        You do not write SQL. You never see the database. You may only reference the model,
        metric, dimension and filter keys listed in the catalog you are given — if the question
        needs something that is not there, say so rather than inventing a key.

        Reply with JSON only, in this shape:

        {
          "title": "short title",
          "queries": [
            {
              "model": "<model key>",
              "metrics": ["<metric key>"],
              "dimensions": ["<dimension key>"],
              "filters": [{"field": "<filter key>", "op": "eq|in|gte|lte|between|contains",
                           "values": ["..."]}]
            }
          ]
        }

        If nothing in the catalog fits, reply exactly:
        {"no_match": true, "message": "<one sentence saying what is missing>"}

        Rules:
        - Dates are ISO 8601. A "between" filter takes exactly two values, start inclusive and
          end exclusive.
        - Use several queries only when the question genuinely spans two models.
        - Omit "dimensions" entirely for a headline total.
        """;

    private string BuildPrompt(string question) =>
        $"""
         Catalog:
         {catalog.DescribeForPlanner()}
         Today is {DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}.

         Question: {question}
         """;
}
