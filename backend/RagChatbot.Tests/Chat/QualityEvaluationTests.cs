using FluentAssertions;
using Moq;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.Chat;
using RagChatbot.Infrastructure.Chat.Tools;

namespace RagChatbot.Tests.Chat;

public class QualityEvaluationTests
{
    private readonly Mock<ILlmService> _mockLlm = new();
    private readonly Mock<IPineconeService> _mockPinecone = new();
    private readonly Mock<IQueryRewriteService> _mockRewriter = new();

    private AgenticRagPipelineService CreateService()
    {
        var searchTool = new SearchKnowledgeBaseTool(_mockPinecone.Object);
        var reformulateTool = new ReformulateQueryTool(_mockRewriter.Object);
        return new AgenticRagPipelineService(_mockLlm.Object, searchTool, reformulateTool);
    }

    private static async IAsyncEnumerable<string> AsyncTokens(params string[] tokens)
    {
        foreach (var token in tokens)
        {
            yield return token;
            await Task.CompletedTask;
        }
    }

    private static async Task<List<SseEvent>> CollectEvents(IAsyncEnumerable<SseEvent> events)
    {
        var result = new List<SseEvent>();
        await foreach (var e in events)
        {
            result.Add(e);
        }
        return result;
    }

    /// <summary>
    /// Sets up a standard search-then-answer flow with high quality scores (passing pre-check).
    /// Call sequence: search tool call -> answer (agent done) -> draft answer -> faithfulness eval -> context recall eval
    /// </summary>
    private void SetupSearchThenAnswer(string searchQuery = "test topic",
        double faithfulness = 0.92, double contextRecall = 0.85)
    {
        var searchToolCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = "{\"query\":\"" + searchQuery + "\",\"top_k\":5}" }
            }
        };

        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "Based on the documents..." };
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "Based on the documents..." };
        var faithfulnessResponse = new LlmToolResponse { HasToolCall = false, Content = $"{{\"score\": {faithfulness}}}" };
        var contextRecallResponse = new LlmToolResponse { HasToolCall = false, Content = $"{{\"score\": {contextRecall}}}" };

        _mockLlm.SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(searchToolCall)
            .ReturnsAsync(answerResponse)
            .ReturnsAsync(draftResponse)
            .ReturnsAsync(faithfulnessResponse)
            .ReturnsAsync(contextRecallResponse);

        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Relevant document content", Metadata = new() { ["source"] = "doc.pdf" }, Score = 0.9 }
            });
    }

    [Fact]
    public void SseEvent_HasFaithfulnessAndContextRecallProperties()
    {
        var evt = new SseEvent
        {
            Type = "quality",
            Faithfulness = 0.92,
            ContextRecall = 0.85
        };

        evt.Faithfulness.Should().Be(0.92);
        evt.ContextRecall.Should().Be(0.85);
    }

    [Fact]
    public void SseEvent_NullableProperties_DefaultToNull()
    {
        var evt = new SseEvent { Type = "quality" };

        evt.Faithfulness.Should().BeNull();
        evt.ContextRecall.Should().BeNull();
    }

    [Fact]
    public async Task ProcessQueryAsync_YieldsQualityEventBeforeDone()
    {
        SetupSearchThenAnswer();

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("tell me about test topic", new List<ChatMessage>()));

        var eventTypes = events.Select(e => e.Type).ToList();

        // Verify order: status(s) -> chunk(s) -> sources -> quality -> done
        var sourcesIndex = eventTypes.IndexOf("sources");
        var qualityIndex = eventTypes.IndexOf("quality");
        var doneIndex = eventTypes.IndexOf("done");

        sourcesIndex.Should().BeGreaterThan(-1, "sources event should exist");
        qualityIndex.Should().BeGreaterThan(-1, "quality event should exist");
        doneIndex.Should().BeGreaterThan(-1, "done event should exist");

        qualityIndex.Should().BeGreaterThan(sourcesIndex, "quality should come after sources");
        doneIndex.Should().BeGreaterThan(qualityIndex, "done should come after quality");
    }

    [Fact]
    public async Task ProcessQueryAsync_QualityEventContainsScores()
    {
        SetupSearchThenAnswer();

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("tell me about test topic", new List<ChatMessage>()));

        var qualityEvent = events.FirstOrDefault(e => e.Type == "quality");
        qualityEvent.Should().NotBeNull();
        qualityEvent!.Faithfulness.Should().Be(0.92);
        qualityEvent.ContextRecall.Should().Be(0.85);
    }

    [Fact]
    public async Task ProcessQueryAsync_InvalidEvalJson_YieldsNullScores_NoRetry()
    {
        // Search then answer, but quality eval returns invalid JSON
        var searchToolCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test","top_k":5}""" }
            }
        };
        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "answer" };
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "answer" };
        var invalidResponse = new LlmToolResponse { HasToolCall = false, Content = "I can't evaluate this properly" };

        _mockLlm.SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(searchToolCall)
            .ReturnsAsync(answerResponse)
            .ReturnsAsync(draftResponse)
            .ReturnsAsync(invalidResponse)   // faithfulness
            .ReturnsAsync(invalidResponse);  // context recall

        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Content", Metadata = new() { ["source"] = "doc.pdf" }, Score = 0.9 }
            });

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("test", new List<ChatMessage>()));

        // Invalid eval JSON -> null scores -> treated as pass -> no retry
        var qualityEvent = events.FirstOrDefault(e => e.Type == "quality");
        qualityEvent.Should().NotBeNull();
        qualityEvent!.Faithfulness.Should().BeNull();
        qualityEvent.ContextRecall.Should().BeNull();

        // Verify no retry happened (no "Improving answer" status event)
        events.Should().NotContain(e => e.Type == "status" && e.Text == "Improving answer with deeper search...");

        events.Last().Type.Should().Be("done");
    }

    [Fact]
    public async Task ProcessQueryAsync_EvalLlmThrows_YieldsNullScores_NoRetry()
    {
        var searchToolCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test","top_k":5}""" }
            }
        };
        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "answer" };
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "answer" };

        var callCount = 0;
        _mockLlm.Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .Returns<List<ChatMessage>, List<ToolDefinition>, float>((msgs, tools, temp) =>
            {
                callCount++;
                if (callCount == 1) return Task.FromResult(searchToolCall);
                if (callCount == 2) return Task.FromResult(answerResponse);
                if (callCount == 3) return Task.FromResult(draftResponse);
                // Quality eval calls throw
                throw new HttpRequestException("LLM service unavailable");
            });

        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Content", Metadata = new() { ["source"] = "doc.pdf" }, Score = 0.9 }
            });

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("test", new List<ChatMessage>()));

        // Eval threw -> null scores -> treated as pass -> no retry
        var qualityEvent = events.FirstOrDefault(e => e.Type == "quality");
        qualityEvent.Should().NotBeNull();
        qualityEvent!.Faithfulness.Should().BeNull();
        qualityEvent.ContextRecall.Should().BeNull();

        events.Last().Type.Should().Be("done");
    }

    [Fact]
    public async Task ProcessQueryAsync_NoSearchResults_SkipsQualityEvent()
    {
        // LLM answers directly without searching — no quality eval
        _mockLlm.Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse { HasToolCall = false, Content = "The answer" });

        _mockLlm.Setup(l => l.StreamCompletionAsync(It.IsAny<List<ChatMessage>>(), It.IsAny<float>()))
            .Returns(AsyncTokens("The answer"));

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("question", new List<ChatMessage>()));

        var qualityEvent = events.FirstOrDefault(e => e.Type == "quality");
        qualityEvent.Should().BeNull("quality event should be skipped when no search context exists");

        events.Last().Type.Should().Be("done");
    }

    [Fact]
    public async Task ProcessQueryAsync_CollectsSearchContextFromMultipleSearches()
    {
        var search1 = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"topic A"}""" }
            }
        };
        var search2 = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_2", Name = "search_knowledge_base", ArgumentsJson = """{"query":"topic B"}""" }
            }
        };
        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "Combined answer" };
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "Combined answer" };
        var faithResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.75}""" };
        var recallResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.85}""" };

        _mockLlm.SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(search1)
            .ReturnsAsync(search2)
            .ReturnsAsync(answerResponse)
            .ReturnsAsync(draftResponse)
            .ReturnsAsync(faithResponse)
            .ReturnsAsync(recallResponse);

        _mockPinecone.Setup(p => p.SimilaritySearchAsync("topic A", It.IsAny<int>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "A content", Metadata = new() { ["source"] = "a.md" }, Score = 0.9 }
            });
        _mockPinecone.Setup(p => p.SimilaritySearchAsync("topic B", It.IsAny<int>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "B content", Metadata = new() { ["source"] = "b.md" }, Score = 0.8 }
            });

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("compare", new List<ChatMessage>()));

        var qualityEvent = events.FirstOrDefault(e => e.Type == "quality");
        qualityEvent.Should().NotBeNull();
        qualityEvent!.Faithfulness.Should().Be(0.75);
        qualityEvent.ContextRecall.Should().Be(0.85);
    }

    [Fact]
    public void SseEvent_WarningProperty_DefaultsToNull()
    {
        var evt = new SseEvent { Type = "quality", Faithfulness = 0.9, ContextRecall = 0.8 };

        evt.Warning.Should().BeNull();
    }

    [Fact]
    public void SseEvent_WarningProperty_CanBeSet()
    {
        var evt = new SseEvent
        {
            Type = "quality",
            Faithfulness = 0.1,
            ContextRecall = 0.2,
            Warning = "This answer may not be fully grounded in the knowledge base"
        };

        evt.Warning.Should().Be("This answer may not be fully grounded in the knowledge base");
    }

    [Fact]
    public async Task ProcessQueryAsync_LowFaithfulness_YieldsWarning()
    {
        // Low faithfulness (0.2 < 0.3) triggers warning
        var searchToolCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test","top_k":5}""" }
            }
        };
        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "answer" };
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "answer" };
        // First quality check: low faithfulness triggers retry
        var faithResponse1 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.2}""" };
        var recallResponse1 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.85}""" };

        // Retry: search again, then draft, then eval
        var retrySearchCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_r1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test","top_k":15}""" }
            }
        };
        var retryAnswerResponse = new LlmToolResponse { HasToolCall = false, Content = "improved answer" };
        var retryDraftResponse = new LlmToolResponse { HasToolCall = false, Content = "improved answer" };
        // Retry eval: still low faithfulness
        var faithResponse2 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.2}""" };
        var recallResponse2 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.85}""" };

        _mockLlm.SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(searchToolCall)       // 1: agent search
            .ReturnsAsync(answerResponse)        // 2: agent done
            .ReturnsAsync(draftResponse)         // 3: draft answer
            .ReturnsAsync(faithResponse1)        // 4: faithfulness pre-check
            .ReturnsAsync(recallResponse1)       // 5: recall pre-check
            .ReturnsAsync(retrySearchCall)       // 6: retry agent search
            .ReturnsAsync(retryAnswerResponse)   // 7: retry agent done
            .ReturnsAsync(retryDraftResponse)    // 8: retry draft
            .ReturnsAsync(faithResponse2)        // 9: retry faithfulness eval
            .ReturnsAsync(recallResponse2);      // 10: retry recall eval

        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Content", Metadata = new() { ["source"] = "doc.pdf" }, Score = 0.9 }
            });

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("test", new List<ChatMessage>()));

        var qualityEvent = events.FirstOrDefault(e => e.Type == "quality");
        qualityEvent.Should().NotBeNull();
        qualityEvent!.Faithfulness.Should().Be(0.2);
        qualityEvent.Warning.Should().Be("This answer may not be fully grounded in the knowledge base");
    }

    [Fact]
    public async Task ProcessQueryAsync_LowContextRecall_YieldsWarning()
    {
        var searchToolCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test","top_k":5}""" }
            }
        };
        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "answer" };
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "answer" };
        // First eval: low context recall
        var faithResponse1 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.85}""" };
        var recallResponse1 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.15}""" };

        // Retry
        var retrySearchCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_r1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test","top_k":15}""" }
            }
        };
        var retryAnswerResponse = new LlmToolResponse { HasToolCall = false, Content = "improved answer" };
        var retryDraftResponse = new LlmToolResponse { HasToolCall = false, Content = "improved answer" };
        var faithResponse2 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.85}""" };
        var recallResponse2 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.15}""" };

        _mockLlm.SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(searchToolCall)
            .ReturnsAsync(answerResponse)
            .ReturnsAsync(draftResponse)
            .ReturnsAsync(faithResponse1)
            .ReturnsAsync(recallResponse1)
            .ReturnsAsync(retrySearchCall)
            .ReturnsAsync(retryAnswerResponse)
            .ReturnsAsync(retryDraftResponse)
            .ReturnsAsync(faithResponse2)
            .ReturnsAsync(recallResponse2);

        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Content", Metadata = new() { ["source"] = "doc.pdf" }, Score = 0.9 }
            });

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("test", new List<ChatMessage>()));

        var qualityEvent = events.FirstOrDefault(e => e.Type == "quality");
        qualityEvent.Should().NotBeNull();
        qualityEvent!.ContextRecall.Should().Be(0.15);
        qualityEvent.Warning.Should().Be("This answer may not be fully grounded in the knowledge base");
    }

    [Fact]
    public async Task ProcessQueryAsync_HighScores_NoWarning()
    {
        SetupSearchThenAnswer();

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("tell me about test topic", new List<ChatMessage>()));

        var qualityEvent = events.FirstOrDefault(e => e.Type == "quality");
        qualityEvent.Should().NotBeNull();
        qualityEvent!.Faithfulness.Should().Be(0.92);
        qualityEvent.ContextRecall.Should().Be(0.85);
        qualityEvent.Warning.Should().BeNull();
    }

    [Fact]
    public async Task ProcessQueryAsync_SearchWithEmptyResults_QualityEvalStillRuns()
    {
        // LLM searches but gets 0 documents back — search context is "Found 0 results..."
        var searchCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"nothing"}""" }
            }
        };
        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "No info found" };
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "No info found" };
        // Quality eval scores are low but both >= 0.7 to avoid triggering retry in this test
        var faithResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.70}""" };
        var recallResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.70}""" };

        _mockLlm.SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(searchCall)
            .ReturnsAsync(answerResponse)
            .ReturnsAsync(draftResponse)
            .ReturnsAsync(faithResponse)
            .ReturnsAsync(recallResponse);

        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<Document>());

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("unknown", new List<ChatMessage>()));

        // Search was called — quality eval should run
        var qualityEvent = events.FirstOrDefault(e => e.Type == "quality");
        qualityEvent.Should().NotBeNull();
        events.Last().Type.Should().Be("done");
    }
}
