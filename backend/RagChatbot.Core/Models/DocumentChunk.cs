namespace RagChatbot.Core.Models;

/// <summary>
/// Represents a chunk of a document with a unique ID, content, and source reference.
/// </summary>
public class DocumentChunk
{
    /// <summary>
    /// Unique ID following the pattern doc_{timestamp}_{index}.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The text content of this chunk.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// The original source (filename or URL) this chunk came from.
    /// </summary>
    public string Source { get; set; } = string.Empty;
}
