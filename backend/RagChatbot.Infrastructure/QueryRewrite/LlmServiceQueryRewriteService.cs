using Microsoft.Extensions.Logging;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Infrastructure.QueryRewrite;

/// <summary>
/// Provider-agnostic query rewriter that delegates to any ILlmService
/// (OpenAI-compatible or Anthropic) via ChatWithToolsAsync with no tools
/// and temperature 0. Falls back to the original query on any failure.
/// The existing OpenAI-hardcoded QueryRewriteService is untouched — this
/// implementation is used by profile-bound interfaces (e.g., the bot).
/// </summary>
public class LlmServiceQueryRewriteService : IQueryRewriteService
{
    private const string SystemPrompt = """
        You are a query rewriter for a document search system.
        Your job is to take a user's natural language question and rewrite it into a clear, search-optimized query.

        Rules:
        - Extract the core intent and topic
        - Expand abbreviations and acronyms
        - Replace slang or informal terms with precise equivalents
        - Remove conversational filler
        - Output ONLY the rewritten query, nothing else — no quotes, no explanation
        """;

    private readonly ILlmService _llm;
    private readonly ILogger<LlmServiceQueryRewriteService> _logger;

    public LlmServiceQueryRewriteService(
        ILlmService llm,
        ILogger<LlmServiceQueryRewriteService> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> RewriteQueryAsync(string originalQuery)
    {
        try
        {
            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = SystemPrompt },
                new() { Role = "user", Content = originalQuery }
            };

            var response = await _llm.ChatWithToolsAsync(
                messages, new List<ToolDefinition>(), temperature: 0.0f);

            var rewritten = response.Content?.Trim();

            if (string.IsNullOrWhiteSpace(rewritten))
            {
                _logger.LogWarning("Query rewrite returned empty response, falling back to original query");
                return originalQuery;
            }

            return rewritten;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Query rewrite failed, falling back to original query");
            return originalQuery;
        }
    }
}
