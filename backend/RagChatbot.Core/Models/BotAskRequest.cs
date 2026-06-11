namespace RagChatbot.Core.Models;

/// <summary>
/// Request body for the POST /bot/ask endpoint.
/// </summary>
public class BotAskRequest
{
    /// <summary>The user's question to ask the RAG system.</summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>Optional project name to filter search results by.</summary>
    public string? Project { get; set; }

    /// <summary>Optional conversation history for context.</summary>
    public List<ChatMessage>? History { get; set; }
}
