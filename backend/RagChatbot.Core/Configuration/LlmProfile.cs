namespace RagChatbot.Core.Configuration;

/// <summary>
/// Describes a named LLM configuration (provider, endpoint, model, key reference).
/// API keys are referenced by environment-variable NAME only — never stored as values.
/// </summary>
public class LlmProfile
{
    /// <summary>Unique profile name (e.g., "default", "claude-answer").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>LLM provider: "openai" (OpenAI-compatible) or "anthropic" (native Messages API).</summary>
    public string Provider { get; set; } = "openai";

    /// <summary>Base URL of the provider API (e.g., "https://api.openai.com/v1").</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Model identifier sent to the provider.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Name of the environment variable that holds the API key.</summary>
    public string ApiKeyEnv { get; set; } = string.Empty;

    /// <summary>
    /// Optional name of a fallback environment variable used when the variable
    /// named by <see cref="ApiKeyEnv"/> is unset or whitespace. Replicates the
    /// AppConfig Effective*ApiKey fallback chain (e.g., REWRITE_LLM_API_KEY → OPENAI_API_KEY).
    /// </summary>
    public string? ApiKeyFallbackEnv { get; set; }

    /// <summary>
    /// Whether the model accepts a temperature parameter.
    /// Some Anthropic models (e.g., opus-4.8) reject temperature; set false for those.
    /// </summary>
    public bool SupportsTemperature { get; set; } = true;

    /// <summary>Maximum output tokens (required by the Anthropic Messages API).</summary>
    public int MaxTokens { get; set; } = 4096;
}
