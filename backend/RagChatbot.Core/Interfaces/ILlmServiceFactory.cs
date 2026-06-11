using RagChatbot.Core.Configuration;

namespace RagChatbot.Core.Interfaces;

/// <summary>
/// Creates ILlmService instances from LLM profiles.
/// Resolves API keys from the environment via the profile's
/// ApiKeyEnv / ApiKeyFallbackEnv chain.
/// </summary>
public interface ILlmServiceFactory
{
    /// <summary>
    /// Creates an ILlmService for the given profile.
    /// Throws InvalidOperationException for unknown providers.
    /// </summary>
    ILlmService Create(LlmProfile profile);
}
