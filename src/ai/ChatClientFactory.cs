using System.ClientModel;
using Anthropic;
using Microsoft.Extensions.AI;
using OpenAI;

namespace SmartReport.Ai;

/// <summary>
/// Resolves the configured provider into an <see cref="IChatClient"/>.
///
/// <para>
/// This is the only file in the solution that names a vendor. Everything above it — the planner,
/// the pipeline, the UI — sees one interface, so adding a provider is a case here rather than a
/// change anywhere else.
/// </para>
/// </summary>
public static class ChatClientFactory
{
    public static IChatClient Create(AiOptions options)
    {
        var provider = options.Provider.ToLowerInvariant();

        return provider switch
        {
            "anthropic" => new AnthropicClient(new Anthropic.Core.ClientOptions { ApiKey = RequireKey(options) })
                .AsIChatClient(options.Model),

            "openai" => new OpenAIClient(new ApiKeyCredential(RequireKey(options)))
                .GetChatClient(options.Model)
                .AsIChatClient(),

            // Groq's free tier: OpenAI-compatible, no card, and fast enough that the planning
            // step does not feel like waiting. The base URL is fixed here so the only thing to
            // configure is a key.
            "groq" => new OpenAIClient(
                    new ApiKeyCredential(RequireKey(options)),
                    new OpenAIClientOptions { Endpoint = new Uri("https://api.groq.com/openai/v1") })
                .GetChatClient(options.Model)
                .AsIChatClient(),

            // Ollama, LM Studio, vLLM, llama.cpp, OpenRouter, Together — anything that speaks the
            // OpenAI chat API. A local server usually ignores the key; the SDK still insists on a
            // non-empty one, so a placeholder is used rather than demanding a secret nobody needs.
            "ollama" or "openai-compatible" => new OpenAIClient(
                    new ApiKeyCredential(string.IsNullOrWhiteSpace(options.ApiKey) ? "not-required" : options.ApiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(options.BaseUrl) })
                .GetChatClient(options.Model)
                .AsIChatClient(),

            _ => throw new InvalidOperationException(
                $"Unknown Ai:Provider '{options.Provider}'. Use scripted, groq, anthropic, openai, "
                + "ollama, or openai-compatible."),
        };
    }

    /// <summary>
    /// Fails at startup rather than on the first question.
    ///
    /// <para>
    /// A missing key is a configuration mistake, and the useful time to hear about it is before
    /// anyone has asked anything.
    /// </para>
    /// </summary>
    private static string RequireKey(AiOptions options) =>
        !string.IsNullOrWhiteSpace(options.ApiKey)
            ? options.ApiKey
            : throw new InvalidOperationException(
                $"Ai:Provider is '{options.Provider}' but no Ai:ApiKey is set. Provide one with "
                + "`dotnet user-secrets set \"Ai:ApiKey\" \"...\"` or the Ai__ApiKey environment "
                + "variable, or set Ai:Provider to 'scripted'.");
}
