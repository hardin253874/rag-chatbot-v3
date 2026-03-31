using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RagChatbot.Core.Configuration;
using RagChatbot.Core.Interfaces;

namespace RagChatbot.Infrastructure.QueryRewrite;

/// <summary>
/// Rewrites user queries using an LLM for better vector search retrieval.
/// Uses OpenAI-compatible chat completions API format.
/// Falls back to the original query on any failure.
/// </summary>
public class QueryRewriteService : IQueryRewriteService
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

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AppConfig _config;
    private readonly ILogger<QueryRewriteService> _logger;

    public QueryRewriteService(
        IHttpClientFactory httpClientFactory,
        AppConfig config,
        ILogger<QueryRewriteService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> RewriteQueryAsync(string originalQuery)
    {
        try
        {
            // Check for missing configuration
            var apiKey = _config.EffectiveRewriteLlmApiKey;
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(_config.RewriteLlmBaseUrl))
            {
                _logger.LogWarning("Query rewrite skipped: missing configuration (API key or base URL)");
                return originalQuery;
            }

            var requestBody = new
            {
                model = _config.RewriteLlmModel,
                messages = new[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = originalQuery }
                },
                temperature = 0,
                max_tokens = 200
            };

            var json = JsonSerializer.Serialize(requestBody);

            var client = _httpClientFactory.CreateClient("OpenAI");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_config.RewriteLlmBaseUrl}/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Query rewrite failed with status {StatusCode}: {Error}",
                    (int)response.StatusCode, errorBody);
                return originalQuery;
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var rewrittenQuery = ParseRewriteResponse(responseJson);

            if (string.IsNullOrWhiteSpace(rewrittenQuery))
            {
                _logger.LogWarning("Query rewrite returned empty response, falling back to original query");
                return originalQuery;
            }

            return rewrittenQuery;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Query rewrite failed, falling back to original query");
            return originalQuery;
        }
    }

    private static string? ParseRewriteResponse(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var choices = doc.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            return null;

        var content = choices[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return content?.Trim();
    }
}
