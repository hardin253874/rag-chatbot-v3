using RagChatbot.Core.Models;

namespace RagChatbot.Core.Interfaces;

/// <summary>
/// Orchestrates the full RAG pipeline: conversational detection, query rewrite,
/// vector search, context assembly, and LLM streaming.
/// </summary>
public interface IRagPipelineService
{
    /// <summary>
    /// Processes a user query through the RAG pipeline and yields SSE events.
    /// </summary>
    /// <param name="question">The user's original question.</param>
    /// <param name="history">The conversation history.</param>
    /// <returns>An async enumerable of SSE events (chunk, sources, done).</returns>
    IAsyncEnumerable<SseEvent> ProcessQueryAsync(string question, List<ChatMessage> history);
}
