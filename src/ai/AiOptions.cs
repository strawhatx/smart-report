namespace SmartReport.Ai;

/// <summary>
/// Which model answers, and how to reach it.
///
/// <para>
/// The application knows nothing about any particular vendor: it resolves an
/// <c>IChatClient</c> and asks. Swapping Claude for GPT for a model on your own hardware is a
/// configuration change, not a code change.
/// </para>
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>
    /// <c>scripted</c> (default, no key needed) · <c>anthropic</c> · <c>openai</c> ·
    /// <c>ollama</c> · <c>openai-compatible</c> (Groq, OpenRouter, Together, vLLM, LM Studio —
    /// anything that speaks the OpenAI chat API, via <see cref="BaseUrl"/>).
    /// </summary>
    public string Provider { get; set; } = "scripted";

    public string Model { get; set; } = "claude-opus-5-5";

    /// <summary>
    /// Never in <c>appsettings.json</c> (C1/C2). Use <c>dotnet user-secrets</c> in development or
    /// an environment variable in production.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>For <c>ollama</c> and <c>openai-compatible</c>. Ignored otherwise.</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434/v1";

    public int TimeoutSeconds { get; set; } = 90;

    public bool IsScripted => string.Equals(Provider, "scripted", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the request leaves this machine. Surfaced in the UI, not hidden.</summary>
    public bool IsHosted => Provider.ToLowerInvariant() is "anthropic" or "openai" or "openai-compatible";
}
