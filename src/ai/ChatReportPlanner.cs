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

    public string ProviderName => _options.Provider.ToLowerInvariant() switch
    {
        "ollama" or "openai-compatible" => $"{_options.Model} (local)",
        _ => _options.Model,
    };

    public async IAsyncEnumerable<PlannerEvent> PlanAsync(
        string question,
        Principal principal,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new PlannerEvent.Thinking($"Asking {ProviderName} to pick measures from the catalog.");

        var chatOptions = new ChatOptions
        {
            Temperature = 0,
            ResponseFormat = ChatResponseFormat.ForJsonSchema(
                CatalogSchema.Build(catalog),
                schemaName: "report_plan",
                schemaDescription: "A report plan expressed only in catalog keys."),
        };

        string content;
        try
        {
            var response = await chat.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, SystemPrompt),
                    new ChatMessage(ChatRole.User, BuildPrompt(question, principal)),
                ],
                chatOptions,
                cancellationToken);

            content = response.Text;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Logged in full, reported narrowly (C5).
            logger.LogError(ex, "Planner provider {Provider} failed.", _options.Provider);
            throw new InvalidOperationException(
                $"The model provider ({_options.Provider}) did not answer. Check Ai:ApiKey and "
                + "Ai:BaseUrl, or set Ai:Provider to 'scripted'.", ex);
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

    private const string SystemPrompt = """
        You translate a question about an order-fulfilment account into a JSON report plan.

        You do not write SQL and you never see the data. You may only use the model, metric,
        dimension and filter keys in the catalog you are given. If the question needs something
        that is not there, set no_match to true and say what is missing.

        Rules:
        - Dates are ISO 8601. A "between" filter takes exactly two values: start inclusive, end
          exclusive.
        - Use more than one query only when the question genuinely spans more than one model.
        - Omit dimensions entirely for a headline total.
        - You do not decide who may see what. Ask for what the question needs; the server decides
          whether the person asking is allowed it, and explains the refusal.
        - Always set every field. Use no_match false and message "" for a normal plan.
        """;

    private string BuildPrompt(string question, Principal principal) =>
        $"""
         Catalog:
         {catalog.DescribeForPlanner()}
         Today is {DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}. The data ends 2026-10-31.

         The question comes from {principal.JobTitle} at {principal.ClientName}.

         Question: {question}
         """;
}
