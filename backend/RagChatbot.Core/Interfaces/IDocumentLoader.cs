using RagChatbot.Core.Models;

namespace RagChatbot.Core.Interfaces;

/// <summary>
/// Loads a document from a file on disk.
/// </summary>
public interface IDocumentLoader
{
    /// <summary>
    /// Reads a file and returns a Document with content and source metadata.
    /// </summary>
    /// <param name="filePath">Path to the file on disk.</param>
    /// <param name="originalFileName">Original filename to use as source metadata.</param>
    Task<Document> LoadAsync(string filePath, string originalFileName);
}
