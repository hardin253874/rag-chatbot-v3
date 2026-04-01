namespace RagChatbot.Core.Models;

/// <summary>
/// Represents a loaded document with its text content and metadata.
/// </summary>
public class Document
{
    public string PageContent { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new();
    public double Score { get; set; }
}
