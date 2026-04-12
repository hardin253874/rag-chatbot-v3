namespace RagChatbot.Core.Models;

/// <summary>
/// Response model for GET /search similarity search results.
/// </summary>
public class SearchResponse
{
    public List<SearchResultItem> Results { get; set; } = new();
    public int Count { get; set; }
}

/// <summary>
/// A single search result item with content, source, project, and similarity score.
/// </summary>
public class SearchResultItem
{
    public string Content { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? Project { get; set; }
    public double Score { get; set; }
}
