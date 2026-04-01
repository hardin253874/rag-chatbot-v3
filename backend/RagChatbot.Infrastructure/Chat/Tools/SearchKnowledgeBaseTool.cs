using System.Text;
using System.Text.Json;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Infrastructure.Chat.Tools;

/// <summary>
/// Agent tool that performs semantic similarity search against Pinecone.
/// Wraps IPineconeService.SimilaritySearchAsync and formats results for LLM consumption.
/// </summary>
public class SearchKnowledgeBaseTool : IAgentTool
{
    private const int DefaultTopK = 5;
    private const int MaxTopK = 10;

    private readonly IPineconeService _pinecone;

    public SearchKnowledgeBaseTool(IPineconeService pinecone)
    {
        _pinecone = pinecone;
    }

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
                    description = "Number of results to return. Default 5, max 10.",
                    @default = 5
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

        var documents = await _pinecone.SimilaritySearchAsync(query, topK);

        if (documents.Count == 0)
            return "Found 0 results for the given query.";

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
}
