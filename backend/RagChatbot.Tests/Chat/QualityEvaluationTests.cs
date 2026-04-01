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

    private void SetupSearchThenAnswer(string searchQuery = "test topic")
    {
        // First call: LLM wants to search
        var searchToolCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = "{\"query\":\"" + searchQuery + "\",\"top_k\":5}" }
            }
        };

        // Second call: LLM answers (main pipeline)
        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "Based on the documents..." };

        // Quality eval calls return scores
        var faithfulnessResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.92}""" };
        var contextRecallResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.85}""" };

        _mockLlm.SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(searchToolCall)
            .ReturnsAsync(answerResponse)
            .ReturnsAsync(faithfulnessResponse)
            .ReturnsAsync(contextRecallResponse);

        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Relevant document content", Metadata = new() { ["source"] = "doc.pdf" }, Score = 0.9 }
            });

        _mockLlm.Setup(l => l.StreamCompletionAsync(It.IsAny<List<ChatMessage>>(), It.IsAny<float>()))
            .Returns(AsyncTokens("Based on the documents..."));
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

        // Verify order: chunk(s) -> sources -> quality -> done
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
    public async Task ProcessQueryAsync_InvalidEvalJson_YieldsNullScores()
    {
        // Setup search then answer
        var searchToolCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test","top_k":5}""" }
            }
        };
        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "answer" };

        // Quality eval returns invalid JSON
        var invalidResponse = new LlmToolResponse { HasToolCall = false, Content = "I can't evaluate this properly" };

        _mockLlm.SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(searchToolCall)
            .ReturnsAsync(answerResponse)
            .ReturnsAsync(invalidResponse)   // faithfulness
            .ReturnsAsync(invalidResponse);  // context recall

        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Content", Metadata = new() { ["source"] = "doc.pdf" }, Score = 0.9 }
            });

        _mockLlm.Setup(l => l.StreamCompletionAsync(It.IsAny<List<ChatMessage>>(), It.IsAny<float>()))
            .Returns(AsyncTokens("answer"));

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("test", new List<ChatMessage>()));

        var qualityEvent = events.FirstOrDefault(e => e.Type == "quality");
        qualityEvent.Should().NotBeNull();
        qualityEvent!.Faithfulness.Should().BeNull();
        qualityEvent.ContextRecall.Should().BeNull();
    }

    [Fact]
    public async Task ProcessQueryAsync_EvalLlmThrows_YieldsNullScores()
    {
        // Setup search then answer
        var searchToolCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test","top_k":5}""" }
            }
        };
        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "answer" };

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
                // Quality eval calls throw
                throw new HttpRequestException("LLM service unavailable");
            });

        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Content", Metadata = new() { ["source"] = "doc.pdf" }, Score = 0.9 }
            });

        _mockLlm.Setup(l => l.StreamCompletionAsync(It.IsAny<List<ChatMessage>>(), It.IsAny<float>()))
            .Returns(AsyncTokens("answer"));

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("test", new List<ChatMessage>()));

        var qualityEvent = events.FirstOrDefault(e => e.Type == "quality");
        qualityEvent.Should().NotBeNull();
        qualityEvent!.Faithfulness.Should().BeNull();
        qualityEvent.ContextRecall.Should().BeNull();

        // Done event should still be present
        events.Last().Type.Should().Be("done");
    }

    [Fact]
    public async Task ProcessQueryAsync_NoSearchResults_YieldsNullQualityScores()
    {
        // LLM answers directly without searching — no context to evaluate
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
        qualityEvent.Should().NotBeNull();
        qualityEvent!.Faithfulness.Should().BeNull();
        qualityEvent.ContextRecall.Should().BeNull();

        events.Last().Type.Should().Be("done");
    }

    [Fact]
    public async Task ProcessQueryAsync_CollectsSearchContextFromMultipleSearches()
    {
        // Two search calls, then answer
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
        var faithResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.75}""" };
        var recallResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.60}""" };

        _mockLlm.SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(search1)
            .ReturnsAsync(search2)
            .ReturnsAsync(answerResponse)
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

        _mockLlm.Setup(l => l.StreamCompletionAsync(It.IsAny<List<ChatMessage>>(), It.IsAny<float>()))
            .Returns(AsyncTokens("Combined answer"));

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("compare", new List<ChatMessage>()));

        var qualityEvent = events.FirstOrDefault(e => e.Type == "quality");
        qualityEvent.Should().NotBeNull();
        qualityEvent!.Faithfulness.Should().Be(0.75);
        qualityEvent.ContextRecall.Should().Be(0.60);
    }
}
