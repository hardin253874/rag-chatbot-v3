namespace RagChatbot.Core.Models;

/// <summary>
/// Request model for raw text ingestion via POST /ingest/text.
/// </summary>
public class IngestTextRequest
{
    public string Content { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? Project { get; set; }
    public string? ChunkingMode { get; set; }
}
