using RagChatbot.Core.Models;

namespace RagChatbot.Core.Interfaces;

/// <summary>
/// Loads a document from a URL by fetching and parsing HTML content.
/// </summary>
public interface IUrlLoader
{
    /// <summary>
    /// Fetches a URL, strips HTML, and returns a Document with visible text content.
    /// </summary>
    /// <param name="url">The URL to fetch.</param>
    Task<Document> LoadAsync(string url);
}
