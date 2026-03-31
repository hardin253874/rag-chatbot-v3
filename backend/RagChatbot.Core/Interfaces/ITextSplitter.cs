using RagChatbot.Core.Models;

namespace RagChatbot.Core.Interfaces;

/// <summary>
/// Splits a Document into smaller DocumentChunks.
/// </summary>
public interface ITextSplitter
{
    /// <summary>
    /// Splits the document content into chunks with generated IDs and source metadata.
    /// </summary>
    List<DocumentChunk> Split(Document document);
}
