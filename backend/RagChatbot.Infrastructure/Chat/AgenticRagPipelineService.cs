using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.Chat.Tools;

namespace RagChatbot.Infrastructure.Chat;

/// <summary>
/// Agentic RAG pipeline that uses LLM function calling to drive the retrieval loop.
/// The LLM decides when to search, when to reformulate, and when to answer.
/// Replaces the linear RagPipelineService.
/// </summary>
public class AgenticRagPipelineService : IRagPipelineService
{
    private const int MaxIterations = 3;

    private const string AgentSystemPrompt = """
        You are a RAG (Retrieval-Augmented Generation) assistant. You answer questions based on documents in a knowledge base.

        You have access to tools for searching the knowledge base. Follow this process:

        1. Analyze the user's question and conversation history
        2. Search the knowledge base using a clear, specific query
        3. Evaluate the retrieved results:
           - Are they relevant to the question?
           - Do they contain enough information to answer?
        4. If results are insufficient, reformulate your query and search again
        5. When you have sufficient context, provide a comprehensive answer

        Guidelines:
        - Use the affirmative form for search queries (statements, not questions)
        - If initial results are poor, try reformulating with different terms
        - Cite information from retrieved documents in your answer
        - If the knowledge base doesn't contain relevant information after searching, say so honestly
        - Do not make up information that isn't in the retrieved documents
        """;

    private readonly ILlmService _llm;
    private readonly SearchKnowledgeBaseTool _searchTool;
    private readonly ReformulateQueryTool _reformulateTool;

    public AgenticRagPipelineService(
        ILlmService llm,
        SearchKnowledgeBaseTool searchTool,
        ReformulateQueryTool reformulateTool)
    {
        _llm = llm;
        _searchTool = searchTool;
        _reformulateTool = reformulateTool;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SseEvent> ProcessQueryAsync(
        string question,
        List<ChatMessage> history)
    {
        // Build initial messages
        var messages = BuildInitialMessages(question, history);

        // Build tool definitions
        var tools = new List<ToolDefinition>
        {
            _searchTool.Definition,
            _reformulateTool.Definition
        };

        // Track sources from all search tool calls
        var allSources = new HashSet<string>();

        // Agent loop (max iterations)
        var iteration = 0;
        var gotAnswer = false;

        while (iteration < MaxIterations && !gotAnswer)
        {
            iteration++;

            var response = await _llm.ChatWithToolsAsync(messages, tools);

            if (response.HasToolCall)
            {
                // Add assistant message with tool calls
                messages.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = string.Empty,
                    ToolCalls = response.ToolCalls
                });

                // Execute each tool call
                foreach (var toolCall in response.ToolCalls)
                {
                    var result = await ExecuteToolAsync(toolCall, allSources);

                    // Add tool result message
                    messages.Add(new ChatMessage
                    {
                        Role = "tool",
                        Content = result,
                        ToolCallId = toolCall.Id
                    });
                }
            }
            else
            {
                // LLM decided to answer
                gotAnswer = true;
            }
        }

        // If max iterations exhausted without answer, force one (call without tools)
        if (!gotAnswer)
        {
            await _llm.ChatWithToolsAsync(messages, new List<ToolDefinition>());
        }

        // Stream the final answer
        await foreach (var token in _llm.StreamCompletionAsync(messages))
        {
            yield return new SseEvent { Type = "chunk", Text = token };
        }

        // Yield deduplicated sources
        yield return new SseEvent
        {
            Type = "sources",
            Sources = allSources.ToList()
        };

        // Done
        yield return new SseEvent { Type = "done" };
    }

    private static List<ChatMessage> BuildInitialMessages(
        string question,
        List<ChatMessage> history)
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = AgentSystemPrompt }
        };

        // Add conversation history
        foreach (var msg in history)
        {
            messages.Add(new ChatMessage
            {
                Role = msg.Role,
                Content = msg.Content
            });
        }

        // Add current question
        messages.Add(new ChatMessage { Role = "user", Content = question });

        return messages;
    }

    private async Task<string> ExecuteToolAsync(ToolCall toolCall, HashSet<string> allSources)
    {
        IAgentTool tool = toolCall.Name switch
        {
            "search_knowledge_base" => _searchTool,
            "reformulate_query" => _reformulateTool,
            _ => throw new InvalidOperationException($"Unknown tool: {toolCall.Name}")
        };

        var result = await tool.ExecuteAsync(toolCall.ArgumentsJson);

        // Track sources from search results
        if (toolCall.Name == "search_knowledge_base")
        {
            ExtractSourcesFromSearchResult(result, allSources);
        }

        return result;
    }

    private static void ExtractSourcesFromSearchResult(string searchResult, HashSet<string> allSources)
    {
        // Parse source names from formatted search results like:
        // [1] (score: 0.87, source: doc.pdf)
        foreach (var line in searchResult.Split('\n'))
        {
            var sourcePrefix = "source: ";
            var sourceIndex = line.IndexOf(sourcePrefix, StringComparison.Ordinal);
            if (sourceIndex >= 0)
            {
                var sourceStart = sourceIndex + sourcePrefix.Length;
                var sourceEnd = line.IndexOf(')', sourceStart);
                if (sourceEnd > sourceStart)
                {
                    var source = line[sourceStart..sourceEnd].Trim();
                    if (!string.IsNullOrWhiteSpace(source))
                    {
                        allSources.Add(source);
                    }
                }
            }
        }
    }
}
