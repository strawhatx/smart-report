namespace SmartReport.Ai;

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>"scripted" (default) or "ollama".</summary>
    public string Provider { get; set; } = "scripted";

    /// <summary>Base address of the local model server. Nothing leaves this machine.</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    public string Model { get; set; } = "qwen2.5";

    /// <summary>
    /// Use the OpenAI-compatible endpoint instead of Ollama's native one, so LM Studio and
    /// llama.cpp work against the same code.
    /// </summary>
    public bool UseOpenAiCompatibleApi { get; set; }

    public int TimeoutSeconds { get; set; } = 90;
}
