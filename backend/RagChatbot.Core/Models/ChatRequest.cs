namespace RagChatbot.Core.Models;

/// <summary>
/// Request body for the POST /chat endpoint.
/// </summary>
public class ChatRequest
{
    /// <summary>
    /// The user's question to ask the RAG system.
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Optional conversation history for context.
    /// </summary>
    public List<ChatMessage>? History { get; set; }
}
