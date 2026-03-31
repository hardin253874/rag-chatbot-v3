namespace RagChatbot.Core.Models;

/// <summary>
/// Represents a Server-Sent Event sent to the client during chat streaming.
/// </summary>
public class SseEvent
{
    /// <summary>
    /// The event type: "chunk", "sources", or "done".
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// The text content for "chunk" events.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// The list of source documents for "sources" events.
    /// </summary>
    public List<string>? Sources { get; set; }
}
