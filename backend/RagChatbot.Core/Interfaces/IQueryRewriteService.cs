namespace RagChatbot.Core.Interfaces;

/// <summary>
/// Rewrites user queries for better vector search retrieval.
/// Falls back to the original query on any failure.
/// </summary>
public interface IQueryRewriteService
{
    /// <summary>
    /// Rewrites a user query into a search-optimized query using an LLM.
    /// On failure (API error, empty response, missing config), returns the original query silently.
    /// </summary>
    /// <param name="originalQuery">The user's original question.</param>
    /// <returns>The rewritten query, or the original query on failure.</returns>
    Task<string> RewriteQueryAsync(string originalQuery);
}
