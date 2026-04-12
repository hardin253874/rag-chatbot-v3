using FluentAssertions;
using Moq;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.Chat;
using RagChatbot.Infrastructure.Chat.Tools;

namespace RagChatbot.Tests.Chat;

public class AgenticRagPipelineServiceTests
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

    [Fact]
    public async Task ProcessQueryAsync_SinglePassAnswer_NoToolUse()
    {
        // LLM answers directly without using any tools (conversational)
        _mockLlm.Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse { HasToolCall = false, Content = "The answer" });

        _mockLlm.Setup(l => l.StreamCompletionAsync(It.IsAny<List<ChatMessage>>(), It.IsAny<float>()))
            .Returns(AsyncTokens("The ", "answer"));

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("question", new List<ChatMessage>()));

        // Should have status, chunk events, sources, and done (no quality since no search)
        var chunks = events.Where(e => e.Type == "chunk").ToList();
        chunks.Should().HaveCountGreaterThanOrEqualTo(1);

        var sources = events.FirstOrDefault(e => e.Type == "sources");
        sources.Should().NotBeNull();
        sources!.Sources.Should().BeEmpty();

        // No quality event for conversational answers
        events.Should().NotContain(e => e.Type == "quality");

        events.Last().Type.Should().Be("done");
    }

    [Fact]
    public async Task ProcessQueryAsync_SearchThenAnswer()
    {
        // First call: LLM wants to search
        var searchToolCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test topic","top_k":5}""" }
            }
        };

        // Second call: LLM answers (agent loop done)
        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "Based on the documents..." };

        // Third call: draft answer (non-streaming)
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "Based on the documents..." };

        // Fourth + Fifth: quality eval
        var faithResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.90}""" };
        var recallResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.85}""" };

        _mockLlm.SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(searchToolCall)
            .ReturnsAsync(answerResponse)
            .ReturnsAsync(draftResponse)
            .ReturnsAsync(faithResponse)
            .ReturnsAsync(recallResponse);

        _mockPinecone.Setup(p => p.SimilaritySearchAsync("test topic", It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Result content", Metadata = new() { ["source"] = "doc.pdf" }, Score = 0.9 }
            });

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("tell me about test topic", new List<ChatMessage>()));

        var chunks = events.Where(e => e.Type == "chunk").ToList();
        chunks.Should().HaveCountGreaterThanOrEqualTo(1);

        var sources = events.First(e => e.Type == "sources");
        sources.Sources.Should().Contain("doc.pdf");

        events.Last().Type.Should().Be("done");
    }

    [Fact]
    public async Task ProcessQueryAsync_ReformulateAndRetry()
    {
        // Call 1: search
        var search1 = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"vague query"}""" }
            }
        };

        // Call 2: reformulate
        var reformulate = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_2", Name = "reformulate_query", ArgumentsJson = """{"query":"vague query","reason":"results not relevant"}""" }
            }
        };

        // Call 3: search again
        var search2 = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_3", Name = "search_knowledge_base", ArgumentsJson = """{"query":"better query"}""" }
            }
        };

        // Call 4: forced answer (max iterations exhausted)
        var forcedAnswer = new LlmToolResponse { HasToolCall = false, Content = "Forced answer" };

        // Call 5: draft answer
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "Forced answer" };

        // Calls 6+7: quality eval
        var faithResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.80}""" };
        var recallResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.75}""" };

        _mockLlm.SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(search1)
            .ReturnsAsync(reformulate)
            .ReturnsAsync(search2)
            .ReturnsAsync(forcedAnswer)
            .ReturnsAsync(draftResponse)
            .ReturnsAsync(faithResponse)
            .ReturnsAsync(recallResponse);

        _mockPinecone.Setup(p => p.SimilaritySearchAsync("vague query", It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Low relevance", Metadata = new() { ["source"] = "a.md" }, Score = 0.3 }
            });

        _mockPinecone.Setup(p => p.SimilaritySearchAsync("better query", It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "High relevance", Metadata = new() { ["source"] = "b.md" }, Score = 0.9 }
            });

        _mockRewriter.Setup(r => r.RewriteQueryAsync("vague query"))
            .ReturnsAsync("better query");

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("test", new List<ChatMessage>()));

        var sources = events.First(e => e.Type == "sources");
        sources.Sources.Should().Contain("a.md");
        sources.Sources.Should().Contain("b.md");
        events.Last().Type.Should().Be("done");
    }

    [Fact]
    public async Task ProcessQueryAsync_MaxIterations_ForcesAnswer()
    {
        // LLM keeps wanting to use tools for all 3 iterations
        var toolCallResponse = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_x", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test"}""" }
            }
        };

        var forcedAnswer = new LlmToolResponse { HasToolCall = false, Content = "Forced" };
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "Forced" };
        var faithResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.80}""" };
        var recallResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.75}""" };

        _mockLlm.SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(toolCallResponse)
            .ReturnsAsync(toolCallResponse)
            .ReturnsAsync(toolCallResponse)
            .ReturnsAsync(forcedAnswer)   // forced (no tools)
            .ReturnsAsync(draftResponse)  // draft answer
            .ReturnsAsync(faithResponse)  // faithfulness eval
            .ReturnsAsync(recallResponse); // context recall eval

        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(new List<Document>());

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("q", new List<ChatMessage>()));

        var chunks = events.Where(e => e.Type == "chunk").ToList();
        chunks.Should().HaveCountGreaterThanOrEqualTo(1);
        events.Last().Type.Should().Be("done");

        // Verify ChatWithToolsAsync was called: 3 agent loop + 1 forced + 1 draft + 2 eval = 7
        _mockLlm.Verify(l => l.ChatWithToolsAsync(
            It.IsAny<List<ChatMessage>>(),
            It.IsAny<List<ToolDefinition>>(),
            It.IsAny<float>()), Times.Exactly(7));
    }

    [Fact]
    public async Task ProcessQueryAsync_EmptySearchResults_AgentAdapts()
    {
        var searchCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"nothing"}""" }
            }
        };

        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "No relevant information found." };
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "No relevant information found." };
        var faithResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.75}""" };
        var recallResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.75}""" };

        _mockLlm.SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(searchCall)
            .ReturnsAsync(answerResponse)
            .ReturnsAsync(draftResponse)
            .ReturnsAsync(faithResponse)
            .ReturnsAsync(recallResponse);

        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(new List<Document>());

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("unknown topic", new List<ChatMessage>()));

        events.Should().Contain(e => e.Type == "chunk");
        events.Last().Type.Should().Be("done");
    }

    [Fact]
    public async Task ProcessQueryAsync_MultipleToolCallsInOneResponse()
    {
        var multiToolCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"topic A"}""" },
                new() { Id = "call_2", Name = "search_knowledge_base", ArgumentsJson = """{"query":"topic B"}""" }
            }
        };

        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "Combined answer" };
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "Combined answer" };
        var faithResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.90}""" };
        var recallResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.85}""" };

        _mockLlm.SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(multiToolCall)
            .ReturnsAsync(answerResponse)
            .ReturnsAsync(draftResponse)
            .ReturnsAsync(faithResponse)
            .ReturnsAsync(recallResponse);

        _mockPinecone.Setup(p => p.SimilaritySearchAsync("topic A", It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "A content", Metadata = new() { ["source"] = "a.md" }, Score = 0.9 }
            });
        _mockPinecone.Setup(p => p.SimilaritySearchAsync("topic B", It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "B content", Metadata = new() { ["source"] = "b.md" }, Score = 0.8 }
            });

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("compare A and B", new List<ChatMessage>()));

        var sources = events.First(e => e.Type == "sources");
        sources.Sources.Should().Contain("a.md");
        sources.Sources.Should().Contain("b.md");
    }

    [Fact]
    public async Task ProcessQueryAsync_ConversationHistoryIncluded()
    {
        List<ChatMessage>? capturedMessages = null;
        _mockLlm.Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .Callback<List<ChatMessage>, List<ToolDefinition>, float>((msgs, _, _) => capturedMessages = msgs)
            .ReturnsAsync(new LlmToolResponse { HasToolCall = false, Content = "answer" });

        _mockLlm.Setup(l => l.StreamCompletionAsync(It.IsAny<List<ChatMessage>>(), It.IsAny<float>()))
            .Returns(AsyncTokens("answer"));

        var history = new List<ChatMessage>
        {
            new() { Role = "user", Content = "Previous question" },
            new() { Role = "assistant", Content = "Previous answer" }
        };

        var service = CreateService();
        await CollectEvents(service.ProcessQueryAsync("new question", history));

        capturedMessages.Should().NotBeNull();
        capturedMessages!.Should().Contain(m => m.Role == "user" && m.Content == "Previous question");
        capturedMessages!.Should().Contain(m => m.Role == "assistant" && m.Content == "Previous answer");
        capturedMessages!.Should().Contain(m => m.Role == "user" && m.Content == "new question");
        capturedMessages!.Should().Contain(m => m.Role == "system");
    }

    // --- B21: Project filter tests ---

    [Fact]
    public async Task ProcessQueryAsync_WithProject_PassesProjectFilterToSearch()
    {
        // Search with project filter
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
        var faithResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.90}""" };
        var recallResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.85}""" };

        _mockLlm.SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(searchToolCall)
            .ReturnsAsync(answerResponse)
            .ReturnsAsync(draftResponse)
            .ReturnsAsync(faithResponse)
            .ReturnsAsync(recallResponse);

        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>(), "NESA"))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "NESA content", Metadata = new() { ["source"] = "nesa.pdf" }, Score = 0.9 }
            });

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("test", new List<ChatMessage>(), project: "NESA"));

        _mockPinecone.Verify(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>(), "NESA"), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessQueryAsync_WithoutProject_PassesNullFilter()
    {
        _mockLlm.Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse { HasToolCall = false, Content = "answer" });

        _mockLlm.Setup(l => l.StreamCompletionAsync(It.IsAny<List<ChatMessage>>(), It.IsAny<float>()))
            .Returns(AsyncTokens("answer"));

        var service = CreateService();
        await CollectEvents(service.ProcessQueryAsync("test", new List<ChatMessage>()));

        // No search happened in this case, but the filter should be null
        _mockPinecone.Verify(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.Is<string?>(s => s != null)), Times.Never);
    }

    [Fact]
    public async Task ProcessQueryAsync_SourcesDeduplicatedAcrossSearches()
    {
        // First search returns source "shared.md"
        var search1 = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"q1"}""" }
            }
        };

        // Second search also returns "shared.md" + "unique.md"
        var search2 = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_2", Name = "search_knowledge_base", ArgumentsJson = """{"query":"q2"}""" }
            }
        };

        var answer = new LlmToolResponse { HasToolCall = false, Content = "answer" };
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "answer" };
        var faithResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.90}""" };
        var recallResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.85}""" };

        _mockLlm.SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(search1)
            .ReturnsAsync(search2)
            .ReturnsAsync(answer)
            .ReturnsAsync(draftResponse)
            .ReturnsAsync(faithResponse)
            .ReturnsAsync(recallResponse);

        _mockPinecone.Setup(p => p.SimilaritySearchAsync("q1", It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "c1", Metadata = new() { ["source"] = "shared.md" }, Score = 0.9 }
            });
        _mockPinecone.Setup(p => p.SimilaritySearchAsync("q2", It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "c2", Metadata = new() { ["source"] = "shared.md" }, Score = 0.8 },
                new() { PageContent = "c3", Metadata = new() { ["source"] = "unique.md" }, Score = 0.7 }
            });

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("q", new List<ChatMessage>()));

        var sources = events.First(e => e.Type == "sources");
        sources.Sources.Should().HaveCount(2);
        sources.Sources.Should().Contain("shared.md");
        sources.Sources.Should().Contain("unique.md");
    }
}
