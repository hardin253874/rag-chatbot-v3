using Microsoft.Extensions.Logging;
using RagChatbot.Core.Configuration;
using RagChatbot.Core.Interfaces;

namespace RagChatbot.Infrastructure.Chat;

/// <summary>
/// Creates ILlmService instances from LLM profiles.
/// - "openai" → OpenAI-compatible LlmService bound to the profile's base URL/model/key
/// - "anthropic" → native AnthropicLlmService
/// API keys are resolved from the environment via ApiKeyEnv, falling back to
/// ApiKeyFallbackEnv when the primary variable is unset or whitespace — the same
/// semantics as AppConfig.Effective*ApiKey.
/// </summary>
public class LlmServiceFactory : ILlmServiceFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;

    public LlmServiceFactory(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public ILlmService Create(LlmProfile profile)
    {
        var apiKey = ResolveApiKey(profile);

        return profile.Provider.ToLowerInvariant() switch
        {
            "openai" => new LlmService(
                _httpClientFactory,
                profile.BaseUrl,
                profile.Model,
                apiKey,
                _loggerFactory.CreateLogger<LlmService>()),

            "anthropic" => new AnthropicLlmService(
                _httpClientFactory,
                profile,
                apiKey,
                _loggerFactory.CreateLogger<AnthropicLlmService>()),

            _ => throw new InvalidOperationException(
                $"Unknown LLM provider '{profile.Provider}' for profile '{profile.Name}'. " +
                "Supported providers: openai, anthropic.")
        };
    }

    /// <summary>
    /// Resolves the API key for a profile: env[ApiKeyEnv] ?? env[ApiKeyFallbackEnv] ?? "".
    /// Whitespace-only values are treated as unset (parity with AppConfig.Effective*ApiKey).
    /// </summary>
    public static string ResolveApiKey(LlmProfile profile)
    {
        var key = string.IsNullOrWhiteSpace(profile.ApiKeyEnv)
            ? null
            : Environment.GetEnvironmentVariable(profile.ApiKeyEnv);

        if (string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(profile.ApiKeyFallbackEnv))
        {
            key = Environment.GetEnvironmentVariable(profile.ApiKeyFallbackEnv);
        }

        return string.IsNullOrWhiteSpace(key) ? string.Empty : key;
    }
}
