using System.Text.Json;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Infrastructure.Chat.Tools;

/// <summary>
/// Agent tool that reformulates a search query for better retrieval results.
/// Wraps IQueryRewriteService.RewriteQueryAsync.
/// </summary>
public class ReformulateQueryTool : IAgentTool
{
    private readonly IQueryRewriteService _rewriter;

    public ReformulateQueryTool(IQueryRewriteService rewriter)
    {
        _rewriter = rewriter;
    }

    public string Name => "reformulate_query";

    public ToolDefinition Definition => new()
    {
        Name = "reformulate_query",
        Description = "Reformulate a search query to improve retrieval results. Use this when initial search results are not relevant enough. Provide the original query and the reason it needs reformulation.",
        ParametersSchema = new
        {
            type = "object",
            properties = new
            {
                query = new
                {
                    type = "string",
                    description = "The original query to reformulate."
                },
                reason = new
                {
                    type = "string",
                    description = "Why the query needs reformulation (e.g., 'results were about X but I need Y')."
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

        var reformulated = await _rewriter.RewriteQueryAsync(query);

        return $"""
            Reformulated query: "{reformulated}"

            Use this reformulated query with search_knowledge_base to find better results.
            """;
    }
}
