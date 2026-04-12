using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RagChatbot.Core.Configuration;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Infrastructure.Chat.Tools;

/// <summary>
/// Agent tool that performs semantic similarity search against Pinecone.
/// Over-fetches results and reranks them using Pinecone Rerank API for improved relevance.
/// Falls back to similarity-ordered results if reranking fails.
/// </summary>
public class SearchKnowledgeBaseTool : IAgentTool
{
    private const int DefaultTopK = 8;
    private const int MaxTopK = 20;

    private readonly IPineconeService _pinecone;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly AppConfig? _config;
    private readonly ILogger? _logger;

    /// <summary>
    /// Creates a SearchKnowledgeBaseTool with reranking support.
    /// </summary>
    public SearchKnowledgeBaseTool(
        IPineconeService pinecone,
        IHttpClientFactory httpClientFactory,
        AppConfig config,
        ILogger<SearchKnowledgeBaseTool>? logger = null)
    {
        _pinecone = pinecone;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Creates a SearchKnowledgeBaseTool without reranking (for backward compatibility / testing).
    /// </summary>
    public SearchKnowledgeBaseTool(IPineconeService pinecone)
    {
        _pinecone = pinecone;
    }

    /// <summary>
    /// Optional project filter to apply to all searches.
    /// Set by the pipeline before entering the agent loop, reset after.
    /// </summary>
    public string? CurrentProjectFilter { get; set; }

    public string Name => "search_knowledge_base";

    public ToolDefinition Definition => new()
    {
        Name = "search_knowledge_base",
        Description = "Search the knowledge base for documents relevant to a query. Returns the most similar document chunks with their content, source, and similarity score. Use specific, descriptive queries in affirmative form for best results.",
        ParametersSchema = new
        {
            type = "object",
            properties = new
            {
                query = new
                {
                    type = "string",
                    description = "The search query. Use clear, specific terms. Prefer affirmative statements over questions."
                },
                top_k = new
                {
                    type = "integer",
                    description = "Number of results to return. Default 8, max 20.",
                    @default = 8
                }
            },
            required = new[] { "query" }
        }
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;

        var query = root.GetProperty("query").GetString() ?? string.Empty;
        var topK = root.TryGetProperty("top_k", out var topKElement)
            ? Math.Min(topKElement.GetInt32(), MaxTopK)
            : DefaultTopK;

        // Over-fetch for reranking
        var fetchK = Math.Min(topK * 2, MaxTopK);
        var documents = await _pinecone.SimilaritySearchAsync(query, fetchK, CurrentProjectFilter);

        if (documents.Count == 0)
            return "Found 0 results for the given query.";

        // Attempt reranking if configured
        if (_httpClientFactory != null && _config != null &&
            !string.IsNullOrWhiteSpace(_config.PineconeApiKey))
        {
            try
            {
                var reranked = await RerankAsync(query, documents, topK);
                if (reranked != null)
                    return FormatResults(reranked);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Pinecone rerank API failed, falling back to similarity order");
            }
        }

        // Fallback: return original similarity-ordered results, trimmed to topK
        var trimmedDocuments = documents.Take(topK).ToList();
        return FormatSimilarityResults(trimmedDocuments);
    }

    private async Task<List<RerankResult>?> RerankAsync(
        string query, List<Document> documents, int topN)
    {
        var client = _httpClientFactory!.CreateClient("PineconeRerank");

        var requestBody = new
        {
            model = "bge-reranker-v2-m3",
            query,
            documents = documents.Select(d => new { text = d.PageContent }).ToArray(),
            top_n = topN
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var request = new HttpRequestMessage(HttpMethod.Post, "rerank")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Api-Key", _config!.PineconeApiKey);

        var response = await client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogWarning("Pinecone rerank API returned {StatusCode}", response.StatusCode);
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        using var responseDoc = JsonDocument.Parse(responseJson);
        var data = responseDoc.RootElement.GetProperty("data");

        var results = new List<RerankResult>();
        foreach (var item in data.EnumerateArray())
        {
            var index = item.GetProperty("index").GetInt32();
            var score = item.GetProperty("score").GetDouble();
            var source = documents[index].Metadata.GetValueOrDefault("source", "unknown");

            results.Add(new RerankResult
            {
                Content = documents[index].PageContent,
                Source = source,
                Score = score
            });
        }

        return results;
    }

    private static string FormatResults(List<RerankResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Found {results.Count} results:");
        sb.AppendLine();

        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"[{i + 1}] (score: {r.Score:F2}, source: {r.Source})");
            sb.AppendLine(r.Content);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatSimilarityResults(List<Document> documents)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Found {documents.Count} results:");
        sb.AppendLine();

        for (var i = 0; i < documents.Count; i++)
        {
            var d = documents[i];
            var source = d.Metadata.GetValueOrDefault("source", "unknown");
            sb.AppendLine($"[{i + 1}] (score: {d.Score:F2}, source: {source})");
            sb.AppendLine(d.PageContent);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private class RerankResult
    {
        public string Content { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public double Score { get; set; }
    }
}
