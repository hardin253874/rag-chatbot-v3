using System.Text.Json.Serialization;

namespace RagChatbot.Core.Models;

/// <summary>
/// Represents a Server-Sent Event sent to the client during chat streaming.
/// </summary>
public class SseEvent
{
    /// <summary>
    /// The event type: "chunk", "sources", "quality", or "done".
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

    /// <summary>
    /// Faithfulness score (0.0-1.0) for "quality" events.
    /// Measures what fraction of claims in the answer are supported by context.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Faithfulness { get; set; }

    /// <summary>
    /// Context recall score (0.0-1.0) for "quality" events.
    /// Measures how much of the needed information is present in retrieved context.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ContextRecall { get; set; }
}
