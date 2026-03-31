using RagChatbot.Core.Models;

namespace RagChatbot.Core.Interfaces;

/// <summary>
/// Service for streaming LLM completions from OpenAI-compatible APIs.
/// </summary>
public interface ILlmService
{
    /// <summary>
    /// Streams a chat completion response token by token.
    /// </summary>
    /// <param name="messages">The conversation messages to send to the LLM.</param>
    /// <param name="temperature">Sampling temperature (default 0.2).</param>
    /// <returns>An async enumerable of content tokens.</returns>
    IAsyncEnumerable<string> StreamCompletionAsync(List<ChatMessage> messages, float temperature = 0.2f);
}
