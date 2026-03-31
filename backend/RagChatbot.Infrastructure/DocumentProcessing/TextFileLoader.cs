using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Infrastructure.DocumentProcessing;

/// <summary>
/// Loads text and markdown files as UTF-8 documents.
/// </summary>
public class TextFileLoader : IDocumentLoader
{
    /// <summary>
    /// Reads a file as UTF-8 text and returns a Document with the original filename as source.
    /// </summary>
    public async Task<Document> LoadAsync(string filePath, string originalFileName)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be empty.", nameof(filePath));

        if (string.IsNullOrWhiteSpace(originalFileName))
            throw new ArgumentException("Original file name must not be empty.", nameof(originalFileName));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}", filePath);

        var content = await File.ReadAllTextAsync(filePath, System.Text.Encoding.UTF8);

        return new Document
        {
            PageContent = content,
            Metadata = new Dictionary<string, string>
            {
                ["source"] = originalFileName
            }
        };
    }
}
