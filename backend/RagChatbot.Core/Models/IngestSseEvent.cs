namespace RagChatbot.Core.Models;

/// <summary>
/// Represents a server-sent event during document ingestion.
/// </summary>
public class IngestSseEvent
{
    /// <summary>
    /// Event type: "status" for progress, "done" for completion, "error" for failure.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable message describing the current stage or result.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Number of chunks produced (only present on "done" events).
    /// </summary>
    public int? Chunks { get; set; }
}
