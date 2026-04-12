using System.Text;
using System.Text.Json;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.Chat.Tools;

namespace RagChatbot.Infrastructure.Chat;

/// <summary>
/// Agentic RAG pipeline that uses LLM function calling to drive the retrieval loop.
/// The LLM decides when to search, when to reformulate, and when to answer.
/// Uses a two-pass search strategy: evaluates draft answer quality before streaming,
/// and retries with deeper search if quality is below threshold.
/// </summary>
public class AgenticRagPipelineService : IRagPipelineService
{
    private const int MaxIterations = 3;
    private const double QualityThreshold = 0.7;

    private const string RetryInstruction =
        "Your previous answer had low quality scores. Search again with a broader query and request more results (top_k=15) to find better context.";

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
        - ONLY answer questions based on information retrieved from the knowledge base documents
        - Do NOT use your own knowledge to answer questions — only use the retrieved context
        - Use the affirmative form for search queries (statements, not questions)
        - If initial results are poor, try reformulating with different terms
        - If the knowledge base doesn't contain relevant information after searching, respond with: "I couldn't find relevant information in the knowledge base to answer this question."
        - Do not make up information that isn't in the retrieved documents
        - Do NOT include source references, citations, footnotes, or bibliographic entries in your answer. Sources are provided separately to the user.
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
        You are an evaluation assistant. Determine how much of the information needed to answer the user's question is present in the retrieved context.

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
        List<ChatMessage> history,
        string? project = null)
    {
        // Set project filter on search tool before entering agent loop
        _searchTool.CurrentProjectFilter = project;

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
        // Track whether any search tool was called
        var searchHappened = false;

        // Yield initial status
        yield return new SseEvent { Type = "status", Text = "Searching knowledge base..." };

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

                    // Track search usage
                    if (toolCall.Name == "search_knowledge_base")
                    {
                        searchHappened = true;
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

        // Branch: conversational (no search) vs search-based
        if (!searchHappened)
        {
            // No search happened — stream directly, skip quality eval
            await foreach (var token in _llm.StreamCompletionAsync(messages))
            {
                yield return new SseEvent { Type = "chunk", Text = token };
            }

            // Yield empty sources
            yield return new SseEvent
            {
                Type = "sources",
                Sources = allSources.ToList()
            };

            // Clean up project filter
            _searchTool.CurrentProjectFilter = null;

            yield return new SseEvent { Type = "done" };
            yield break;
        }

        // Search happened — two-pass quality evaluation flow
        var fullContext = string.Join("\n\n", searchContextParts);

        // Get draft answer (non-streaming)
        yield return new SseEvent { Type = "status", Text = "Evaluating answer quality..." };
        var draftResponse = await _llm.ChatWithToolsAsync(messages, new List<ToolDefinition>());
        var draftAnswer = draftResponse.Content ?? string.Empty;

        // Run quality pre-check
        var (faithfulness, contextRecall) = await EvaluateScoresAsync(question, draftAnswer, fullContext, history);

        // Check if quality passes threshold
        var qualityPasses = QualityPassesThreshold(faithfulness, contextRecall);

        SseEvent qualityEvent;

        if (!qualityPasses)
        {
            // Quality too low — retry with deeper search
            yield return new SseEvent { Type = "status", Text = "Improving answer with deeper search..." };

            // Add retry instruction to messages
            messages.Add(new ChatMessage
            {
                Role = "system",
                Content = RetryInstruction
            });

            // Run agent loop again (max 3 more iterations)
            var retryIteration = 0;
            var retryGotAnswer = false;

            while (retryIteration < MaxIterations && !retryGotAnswer)
            {
                retryIteration++;

                var retryResponse = await _llm.ChatWithToolsAsync(messages, tools);

                if (retryResponse.HasToolCall)
                {
                    messages.Add(new ChatMessage
                    {
                        Role = "assistant",
                        Content = string.Empty,
                        ToolCalls = retryResponse.ToolCalls
                    });

                    foreach (var toolCall in retryResponse.ToolCalls)
                    {
                        var result = await ExecuteToolAsync(toolCall, allSources);

                        if (toolCall.Name == "search_knowledge_base")
                        {
                            searchContextParts.Add(result);
                        }

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
                    retryGotAnswer = true;
                }
            }

            if (!retryGotAnswer)
            {
                await _llm.ChatWithToolsAsync(messages, new List<ToolDefinition>());
            }

            // Get new draft answer
            var retryDraftResponse = await _llm.ChatWithToolsAsync(messages, new List<ToolDefinition>());
            draftAnswer = retryDraftResponse.Content ?? string.Empty;

            // Re-evaluate quality on new draft with updated context
            var updatedContext = string.Join("\n\n", searchContextParts);
            var (retryFaithfulness, retryContextRecall) = await EvaluateScoresAsync(question, draftAnswer, updatedContext, history);

            // Build quality event from retry eval (accept even if still low)
            qualityEvent = BuildQualityEvent(retryFaithfulness, retryContextRecall);
        }
        else
        {
            // Quality passes — use the draft as-is
            qualityEvent = BuildQualityEvent(faithfulness, contextRecall);
        }

        // Stream the final draft as chunk events (simulated streaming)
        foreach (var chunk in SplitForStreaming(draftAnswer))
        {
            yield return new SseEvent { Type = "chunk", Text = chunk };
        }

        // Yield sources
        yield return new SseEvent
        {
            Type = "sources",
            Sources = allSources.ToList()
        };

        // Yield quality
        yield return qualityEvent;

        // Clean up project filter
        _searchTool.CurrentProjectFilter = null;

        // Done
        yield return new SseEvent { Type = "done" };
    }

    /// <summary>
    /// Checks whether both quality scores pass the threshold.
    /// If either score is null (eval failed), treat as passing.
    /// </summary>
    private static bool QualityPassesThreshold(double? faithfulness, double? contextRecall)
    {
        // If eval failed (null), treat as passing — don't retry on eval failure
        var faithPasses = !faithfulness.HasValue || faithfulness.Value >= QualityThreshold;
        var recallPasses = !contextRecall.HasValue || contextRecall.Value >= QualityThreshold;
        return faithPasses && recallPasses;
    }

    /// <summary>
    /// Evaluates faithfulness and context recall scores in parallel.
    /// Returns (null, null) if evaluation fails entirely.
    /// </summary>
    private async Task<(double? faithfulness, double? contextRecall)> EvaluateScoresAsync(
        string question, string answer, string context, List<ChatMessage>? history = null)
    {
        if (string.IsNullOrWhiteSpace(answer) || string.IsNullOrWhiteSpace(context))
        {
            return (null, null);
        }

        try
        {
            var faithfulnessTask = EvaluateFaithfulnessAsync(context, answer);
            var contextRecallTask = EvaluateContextRecallAsync(question, context, history);

            await Task.WhenAll(faithfulnessTask, contextRecallTask);

            return (faithfulnessTask.Result, contextRecallTask.Result);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Builds a quality SseEvent from evaluation scores.
    /// </summary>
    private static SseEvent BuildQualityEvent(double? faithfulness, double? contextRecall)
    {
        string? warning = null;
        if ((faithfulness.HasValue && faithfulness < 0.3) ||
            (contextRecall.HasValue && contextRecall < 0.3))
        {
            warning = "This answer may not be fully grounded in the knowledge base";
        }

        return new SseEvent
        {
            Type = "quality",
            Faithfulness = faithfulness,
            ContextRecall = contextRecall,
            Warning = warning
        };
    }

    /// <summary>
    /// Splits text into small chunks for simulated streaming.
    /// Yields approximately every 20 characters, splitting on word boundaries.
    /// </summary>
    internal static IEnumerable<string> SplitForStreaming(string text)
    {
        if (string.IsNullOrEmpty(text))
            yield break;

        var words = text.Split(' ');
        var buffer = new StringBuilder();
        foreach (var word in words)
        {
            if (buffer.Length > 0)
                buffer.Append(' ');
            buffer.Append(word);

            if (buffer.Length >= 20)
            {
                yield return buffer.ToString();
                buffer.Clear();
            }
        }

        if (buffer.Length > 0)
            yield return buffer.ToString();
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

    private async Task<double?> EvaluateContextRecallAsync(string question, string context, List<ChatMessage>? history = null)
    {
        try
        {
            // For follow-up instructions like "answer again", "improve", "more detail",
            // use the original substantive question from history for evaluation.
            var evaluationQuestion = ResolveEvaluationQuestion(question, history);

            var prompt = ContextRecallPrompt
                .Replace("{question}", evaluationQuestion)
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

    /// <summary>
    /// Resolves the actual topic question for evaluation. If the current question looks like
    /// a meta-instruction (e.g., "answer again", "improve"), returns the first substantive
    /// user question from conversation history instead.
    /// </summary>
    private static string ResolveEvaluationQuestion(string currentQuestion, List<ChatMessage>? history)
    {
        if (history == null || history.Count == 0)
            return currentQuestion;

        // Check if current question looks like a meta-instruction (short, no topic keywords)
        var lower = currentQuestion.ToLowerInvariant();
        var metaPatterns = new[]
        {
            "answer again", "try again", "think again", "improve", "more detail",
            "better answer", "not good enough", "too low", "retry", "redo",
            "elaborate", "expand", "tell me more", "go deeper"
        };

        var isMetaInstruction = metaPatterns.Any(p => lower.Contains(p));

        if (!isMetaInstruction)
            return currentQuestion;

        // Find the first substantive user question from history (scan from earliest)
        var firstUserQuestion = history
            .Where(m => m.Role == "user")
            .Select(m => m.Content)
            .FirstOrDefault(content =>
            {
                var l = content.ToLowerInvariant();
                return !metaPatterns.Any(p => l.Contains(p));
            });

        return firstUserQuestion ?? currentQuestion;
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
