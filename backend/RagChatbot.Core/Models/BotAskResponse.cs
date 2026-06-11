namespace RagChatbot.Core.Models;

/// <summary>
/// Response body for the POST /bot/ask endpoint — a single JSON object
/// aggregated from the same SSE pipeline used by /chat.
/// </summary>
public class BotAskResponse
{
    /// <summary>The full answer text (all chunk events concatenated).</summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>Deduplicated source document names.</summary>
    public List<string> Sources { get; set; } = new();

    /// <summary>
    /// Final quality scores, or null when no quality event was emitted
    /// (e.g., conversational answers with no search context).
    /// Serialized as an explicit null so consumers see "quality": null.
    /// </summary>
    public BotQuality? Quality { get; set; }
}

/// <summary>
/// Quality scores for a bot answer.
/// </summary>
public class BotQuality
{
    public double? Faithfulness { get; set; }
    public double? ContextRecall { get; set; }
    public string? Warning { get; set; }
}
