using System.Text;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Infrastructure.Chat;

/// <summary>
/// Orchestrates the full RAG pipeline: conversational detection, query rewrite,
/// vector search, context assembly, and LLM streaming.
/// Yields SSE events (chunk, sources, done) as an async enumerable.
/// </summary>
public class RagPipelineService : IRagPipelineService
{
    private const string RagSystemPrompt = """
        You are a helpful assistant. Answer the question using the context provided below.
        Focus on the core topic of the question and use any relevant information from the context to provide a helpful answer.
        If the context is partially relevant, answer with what you can and note any gaps.
        Only refuse to answer if the context has absolutely nothing to do with the question.
        """;

    private const string ConversationalSystemPrompt =
        "You are a helpful assistant. Based on the conversation below, answer the user's latest question.";

    private const string NoResultsMessage =
        "I couldn't find any relevant information in the knowledge base.";

    private readonly IConversationalDetector _detector;
    private readonly IQueryRewriteService _rewriter;
    private readonly IPineconeService _pinecone;
    private readonly ILlmService _llm;

    public RagPipelineService(
        IConversationalDetector detector,
        IQueryRewriteService rewriter,
        IPineconeService pinecone,
        ILlmService llm)
    {
        _detector = detector;
        _rewriter = rewriter;
        _pinecone = pinecone;
        _llm = llm;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SseEvent> ProcessQueryAsync(
        string question,
        List<ChatMessage> history)
    {
        // Step 1: Check if conversational follow-up with history
        if (_detector.IsFollowUp(question) && history.Count > 0)
        {
            // Conversational path — skip vector search
            var messages = BuildConversationalPrompt(question, history);

            await foreach (var token in _llm.StreamCompletionAsync(messages, 0.2f))
            {
                yield return new SseEvent { Type = "chunk", Text = token };
            }

            yield return new SseEvent { Type = "done" };
            yield break;
        }

        // Step 2: Rewrite query for vector search
        var rewrittenQuery = await _rewriter.RewriteQueryAsync(question);

        // Step 3: Search Pinecone with rewritten query
        var documents = await _pinecone.SimilaritySearchAsync(rewrittenQuery, 5);

        // Step 4: Handle empty results
        if (documents.Count == 0)
        {
            yield return new SseEvent { Type = "chunk", Text = NoResultsMessage };
            yield return new SseEvent { Type = "done" };
            yield break;
        }

        // Step 5: Build prompt with original question (not rewritten — ADR-003)
        var llmMessages = BuildRagPrompt(question, history, documents);

        // Step 6: Stream LLM response
        await foreach (var token in _llm.StreamCompletionAsync(llmMessages, 0.2f))
        {
            yield return new SseEvent { Type = "chunk", Text = token };
        }

        // Step 7: Yield sources (deduplicated)
        var sources = documents
            .Select(d => d.Metadata.GetValueOrDefault("source", ""))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();

        yield return new SseEvent { Type = "sources", Sources = sources };

        // Step 8: Done
        yield return new SseEvent { Type = "done" };
    }

    private static List<ChatMessage> BuildRagPrompt(
        string question,
        List<ChatMessage> history,
        List<Document> documents)
    {
        var contextBuilder = new StringBuilder();
        contextBuilder.AppendLine(RagSystemPrompt);
        contextBuilder.AppendLine();
        contextBuilder.AppendLine("Context:");

        for (var i = 0; i < documents.Count; i++)
        {
            contextBuilder.AppendLine($"[{i + 1}] {documents[i].PageContent}");
        }

        if (history.Count > 0)
        {
            contextBuilder.AppendLine();
            contextBuilder.AppendLine("Conversation so far:");

            foreach (var msg in history)
            {
                var role = msg.Role == "user" ? "User" : "Assistant";
                contextBuilder.AppendLine($"{role}: {msg.Content}");
            }
        }

        contextBuilder.AppendLine();
        contextBuilder.AppendLine($"Question: {question}");

        return new List<ChatMessage>
        {
            new() { Role = "system", Content = contextBuilder.ToString() },
            new() { Role = "user", Content = question }
        };
    }

    private static List<ChatMessage> BuildConversationalPrompt(
        string question,
        List<ChatMessage> history)
    {
        var contextBuilder = new StringBuilder();
        contextBuilder.AppendLine(ConversationalSystemPrompt);
        contextBuilder.AppendLine();
        contextBuilder.AppendLine("Conversation:");

        foreach (var msg in history)
        {
            var role = msg.Role == "user" ? "User" : "Assistant";
            contextBuilder.AppendLine($"{role}: {msg.Content}");
        }

        contextBuilder.AppendLine();
        contextBuilder.AppendLine($"Question: {question}");

        return new List<ChatMessage>
        {
            new() { Role = "system", Content = contextBuilder.ToString() },
            new() { Role = "user", Content = question }
        };
    }
}
