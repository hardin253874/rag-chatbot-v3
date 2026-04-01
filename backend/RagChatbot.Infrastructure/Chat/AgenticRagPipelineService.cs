using System.Text;
using System.Text.Json;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.Chat.Tools;

namespace RagChatbot.Infrastructure.Chat;

/// <summary>
/// Agentic RAG pipeline that uses LLM function calling to drive the retrieval loop.
/// The LLM decides when to search, when to reformulate, and when to answer.
/// After streaming, runs parallel quality evaluation (faithfulness + context recall).
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
        - If the knowledge base doesn't contain relevant information after searching, say so honestly
        - Do not make up information that isn't in the retrieved documents
        - Do NOT include source references, citations, footnotes, or bibliographic entries in your answer (e.g., no [1], [Source: ...], (Source: ...), or "According to document X"). Sources are provided separately to the user.
        """;

    private const string FaithfulnessPrompt = """
        You are an evaluation assistant. Given the retrieved context and the generated answer, determine what fraction of the claims in the answer are supported by the retrieved context.

        Score from 0.0 to 1.0:
        - 1.0 = every claim in the answer is directly supported by the context
        - 0.5 = about half of the claims are supported
        - 0.0 = none of the claims are supported by the context

        Retrieved context:
        ---
        {context_chunks}
        ---

        Generated answer:
        ---
        {answer_text}
        ---

        Return ONLY a JSON object: {"score": 0.XX}
        """;

    private const string ContextRecallPrompt = """
        You are an evaluation assistant. Given the user's question and the retrieved context, determine how much of the information needed to answer the question is present in the retrieved context.

        Score from 0.0 to 1.0:
        - 1.0 = the context contains all information needed to fully answer the question
        - 0.5 = the context contains about half of what's needed
        - 0.0 = the context contains nothing relevant to the question

        User question:
        ---
        {question}
        ---

        Retrieved context:
        ---
        {context_chunks}
        ---

        Return ONLY a JSON object: {"score": 0.XX}
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
        // Track search context for quality evaluation
        var searchContextParts = new List<string>();

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

                    // Collect search context for quality evaluation
                    if (toolCall.Name == "search_knowledge_base")
                    {
                        searchContextParts.Add(result);
                    }

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

        // Stream the final answer and accumulate full text
        var answerBuilder = new StringBuilder();
        await foreach (var token in _llm.StreamCompletionAsync(messages))
        {
            answerBuilder.Append(token);
            yield return new SseEvent { Type = "chunk", Text = token };
        }

        // Yield deduplicated sources
        yield return new SseEvent
        {
            Type = "sources",
            Sources = allSources.ToList()
        };

        // Run quality evaluation (parallel faithfulness + context recall)
        var fullAnswer = answerBuilder.ToString();
        var fullContext = string.Join("\n\n", searchContextParts);
        var qualityEvent = await EvaluateQualityAsync(question, fullAnswer, fullContext);
        yield return qualityEvent;

        // Done
        yield return new SseEvent { Type = "done" };
    }

    private async Task<SseEvent> EvaluateQualityAsync(string question, string answer, string context)
    {
        if (string.IsNullOrWhiteSpace(answer) || string.IsNullOrWhiteSpace(context))
        {
            return new SseEvent { Type = "quality", Faithfulness = null, ContextRecall = null };
        }

        try
        {
            var faithfulnessTask = EvaluateFaithfulnessAsync(context, answer);
            var contextRecallTask = EvaluateContextRecallAsync(question, context);

            await Task.WhenAll(faithfulnessTask, contextRecallTask);

            return new SseEvent
            {
                Type = "quality",
                Faithfulness = faithfulnessTask.Result,
                ContextRecall = contextRecallTask.Result
            };
        }
        catch
        {
            return new SseEvent { Type = "quality", Faithfulness = null, ContextRecall = null };
        }
    }

    private async Task<double?> EvaluateFaithfulnessAsync(string context, string answer)
    {
        try
        {
            var prompt = FaithfulnessPrompt
                .Replace("{context_chunks}", context)
                .Replace("{answer_text}", answer);

            var messages = new List<ChatMessage>
            {
                new() { Role = "user", Content = prompt }
            };

            var response = await _llm.ChatWithToolsAsync(messages, new List<ToolDefinition>(), temperature: 0.0f);
            return ParseScore(response.Content);
        }
        catch
        {
            return null;
        }
    }

    private async Task<double?> EvaluateContextRecallAsync(string question, string context)
    {
        try
        {
            var prompt = ContextRecallPrompt
                .Replace("{question}", question)
                .Replace("{context_chunks}", context);

            var messages = new List<ChatMessage>
            {
                new() { Role = "user", Content = prompt }
            };

            var response = await _llm.ChatWithToolsAsync(messages, new List<ToolDefinition>(), temperature: 0.0f);
            return ParseScore(response.Content);
        }
        catch
        {
            return null;
        }
    }

    private static double? ParseScore(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("score", out var scoreElement))
            {
                return scoreElement.GetDouble();
            }
            return null;
        }
        catch
        {
            return null;
        }
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
