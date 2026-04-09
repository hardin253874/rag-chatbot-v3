using FluentAssertions;
using Moq;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.Chat;
using RagChatbot.Infrastructure.Chat.Tools;

namespace RagChatbot.Tests.Chat;

/// <summary>
/// Tests for the two-pass adaptive quality search feature.
/// Validates pre-check quality evaluation, retry with deeper search,
/// status events, draft streaming, and edge cases.
/// </summary>
public class TwoPassSearchTests
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
    public async Task HighQualityPassesDirectly_NoDraftRetry()
    {
        // Both scores >= 0.7, draft should be streamed directly without retry
        var searchToolCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test","top_k":5}""" }
            }
        };
        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "Good answer" };
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "Good answer based on documents" };
        var faithResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.85}""" };
        var recallResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.90}""" };

        _mockLlm.SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(searchToolCall)
            .ReturnsAsync(answerResponse)
            .ReturnsAsync(draftResponse)
            .ReturnsAsync(faithResponse)
            .ReturnsAsync(recallResponse);

        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Relevant content", Metadata = new() { ["source"] = "doc.pdf" }, Score = 0.9 }
            });

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("test question", new List<ChatMessage>()));

        // Verify no retry status event
        events.Should().NotContain(e => e.Type == "status" && e.Text == "Improving answer with deeper search...");

        // Quality event should have passing scores
        var qualityEvent = events.First(e => e.Type == "quality");
        qualityEvent.Faithfulness.Should().Be(0.85);
        qualityEvent.ContextRecall.Should().Be(0.90);

        // Draft answer content should appear in chunks
        var chunkText = string.Join("", events.Where(e => e.Type == "chunk").Select(e => e.Text));
        chunkText.Should().Contain("Good answer");

        // Total ChatWithToolsAsync calls: 1 search + 1 answer + 1 draft + 2 eval = 5
        _mockLlm.Verify(l => l.ChatWithToolsAsync(
            It.IsAny<List<ChatMessage>>(),
            It.IsAny<List<ToolDefinition>>(),
            It.IsAny<float>()), Times.Exactly(5));

        events.Last().Type.Should().Be("done");
    }

    [Fact]
    public async Task LowFaithfulness_TriggersRetry()
    {
        // Faithfulness < 0.7 triggers retry
        var searchToolCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test","top_k":5}""" }
            }
        };
        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "answer" };
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "Poor draft answer" };
        var faithResponse1 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.50}""" };
        var recallResponse1 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.90}""" };

        // Retry: search again
        var retrySearchCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_r1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test","top_k":15}""" }
            }
        };
        var retryAnswerResponse = new LlmToolResponse { HasToolCall = false, Content = "improved" };
        var retryDraftResponse = new LlmToolResponse { HasToolCall = false, Content = "Improved answer with better context" };
        var faithResponse2 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.85}""" };
        var recallResponse2 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.90}""" };

        _mockLlm.SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(searchToolCall)        // 1: agent search
            .ReturnsAsync(answerResponse)         // 2: agent done
            .ReturnsAsync(draftResponse)          // 3: draft
            .ReturnsAsync(faithResponse1)         // 4: pre-check faithfulness
            .ReturnsAsync(recallResponse1)        // 5: pre-check recall
            .ReturnsAsync(retrySearchCall)        // 6: retry agent search
            .ReturnsAsync(retryAnswerResponse)    // 7: retry agent done
            .ReturnsAsync(retryDraftResponse)     // 8: retry draft
            .ReturnsAsync(faithResponse2)         // 9: retry faithfulness
            .ReturnsAsync(recallResponse2);       // 10: retry recall

        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Better content", Metadata = new() { ["source"] = "better.pdf" }, Score = 0.95 }
            });

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("test", new List<ChatMessage>()));

        // Retry status event should be present
        events.Should().Contain(e => e.Type == "status" && e.Text == "Improving answer with deeper search...");

        // Final quality from retry eval
        var qualityEvent = events.First(e => e.Type == "quality");
        qualityEvent.Faithfulness.Should().Be(0.85);
        qualityEvent.ContextRecall.Should().Be(0.90);

        // Final answer should be the retry draft
        var chunkText = string.Join("", events.Where(e => e.Type == "chunk").Select(e => e.Text));
        chunkText.Should().Contain("Improved answer");

        events.Last().Type.Should().Be("done");
    }

    [Fact]
    public async Task LowContextRecall_TriggersRetry()
    {
        // Context recall < 0.7 triggers retry
        var searchToolCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test","top_k":5}""" }
            }
        };
        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "answer" };
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "Draft with insufficient context" };
        var faithResponse1 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.90}""" };
        var recallResponse1 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.40}""" };

        // Retry
        var retrySearchCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_r1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test broader","top_k":15}""" }
            }
        };
        var retryAnswerResponse = new LlmToolResponse { HasToolCall = false, Content = "improved" };
        var retryDraftResponse = new LlmToolResponse { HasToolCall = false, Content = "Better answer with full context" };
        var faithResponse2 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.88}""" };
        var recallResponse2 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.80}""" };

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

        events.Should().Contain(e => e.Type == "status" && e.Text == "Improving answer with deeper search...");

        var qualityEvent = events.First(e => e.Type == "quality");
        qualityEvent.Faithfulness.Should().Be(0.88);
        qualityEvent.ContextRecall.Should().Be(0.80);

        var chunkText = string.Join("", events.Where(e => e.Type == "chunk").Select(e => e.Text));
        chunkText.Should().Contain("Better answer");

        events.Last().Type.Should().Be("done");
    }

    [Fact]
    public async Task NoSearch_SkipsPreCheck_StreamsDirectly()
    {
        // No search_knowledge_base calls — stream directly without quality eval
        _mockLlm.Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse { HasToolCall = false, Content = "Hi there!" });

        _mockLlm.Setup(l => l.StreamCompletionAsync(It.IsAny<List<ChatMessage>>(), It.IsAny<float>()))
            .Returns(AsyncTokens("Hi ", "there!"));

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("hello", new List<ChatMessage>()));

        // No quality event
        events.Should().NotContain(e => e.Type == "quality");

        // No "Evaluating answer quality..." status
        events.Should().NotContain(e => e.Type == "status" && e.Text == "Evaluating answer quality...");

        // Chunks should come from StreamCompletionAsync
        var chunks = events.Where(e => e.Type == "chunk").ToList();
        chunks.Should().HaveCount(2);
        chunks[0].Text.Should().Be("Hi ");
        chunks[1].Text.Should().Be("there!");

        events.Last().Type.Should().Be("done");
    }

    [Fact]
    public async Task StatusEvents_EmittedInCorrectOrder()
    {
        // Search-based flow with high quality (no retry)
        var searchToolCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test","top_k":5}""" }
            }
        };
        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "answer" };
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "The answer is here" };
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

        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Content", Metadata = new() { ["source"] = "doc.pdf" }, Score = 0.9 }
            });

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("test", new List<ChatMessage>()));

        var eventTypes = events.Select(e => e.Type).ToList();

        // Expected order: status("Searching...") -> status("Evaluating...") -> chunk(s) -> sources -> quality -> done
        var searchingStatusIdx = events.FindIndex(e => e.Type == "status" && e.Text == "Searching knowledge base...");
        var evaluatingStatusIdx = events.FindIndex(e => e.Type == "status" && e.Text == "Evaluating answer quality...");
        var firstChunkIdx = eventTypes.IndexOf("chunk");
        var sourcesIdx = eventTypes.IndexOf("sources");
        var qualityIdx = eventTypes.IndexOf("quality");
        var doneIdx = eventTypes.IndexOf("done");

        searchingStatusIdx.Should().Be(0, "first event should be 'Searching...' status");
        evaluatingStatusIdx.Should().BeGreaterThan(searchingStatusIdx);
        firstChunkIdx.Should().BeGreaterThan(evaluatingStatusIdx);
        sourcesIdx.Should().BeGreaterThan(firstChunkIdx);
        qualityIdx.Should().BeGreaterThan(sourcesIdx);
        doneIdx.Should().BeGreaterThan(qualityIdx);
    }

    [Fact]
    public async Task EvalFailure_TreatedAsPass_NoRetry()
    {
        // When quality eval returns bad JSON, treat as pass (don't retry)
        var searchToolCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test","top_k":5}""" }
            }
        };
        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "answer" };
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "Draft answer text" };
        var invalidResponse = new LlmToolResponse { HasToolCall = false, Content = "Sorry I cannot evaluate" };

        _mockLlm.SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(searchToolCall)
            .ReturnsAsync(answerResponse)
            .ReturnsAsync(draftResponse)
            .ReturnsAsync(invalidResponse)  // faithfulness: bad JSON
            .ReturnsAsync(invalidResponse); // context recall: bad JSON

        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Content", Metadata = new() { ["source"] = "doc.pdf" }, Score = 0.9 }
            });

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("test", new List<ChatMessage>()));

        // Should NOT have retry status
        events.Should().NotContain(e => e.Type == "status" && e.Text == "Improving answer with deeper search...");

        // Quality event with null scores
        var qualityEvent = events.First(e => e.Type == "quality");
        qualityEvent.Faithfulness.Should().BeNull();
        qualityEvent.ContextRecall.Should().BeNull();

        // Draft should still be streamed as chunks
        var chunkText = string.Join("", events.Where(e => e.Type == "chunk").Select(e => e.Text));
        chunkText.Should().Contain("Draft answer");

        // Only 5 ChatWithToolsAsync calls (no retry)
        _mockLlm.Verify(l => l.ChatWithToolsAsync(
            It.IsAny<List<ChatMessage>>(),
            It.IsAny<List<ToolDefinition>>(),
            It.IsAny<float>()), Times.Exactly(5));

        events.Last().Type.Should().Be("done");
    }

    [Fact]
    public async Task MaxOneRetry_StillLowQuality_AcceptedWithWarning()
    {
        // Even if retry quality is still low, accept it (only 1 retry max)
        var searchToolCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test","top_k":5}""" }
            }
        };
        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "answer" };
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "Poor answer" };
        // First eval: low
        var faithResponse1 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.20}""" };
        var recallResponse1 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.30}""" };

        // Retry
        var retrySearchCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_r1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test","top_k":15}""" }
            }
        };
        var retryAnswerResponse = new LlmToolResponse { HasToolCall = false, Content = "still poor" };
        var retryDraftResponse = new LlmToolResponse { HasToolCall = false, Content = "Still poor answer" };
        // Retry eval: still low
        var faithResponse2 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.25}""" };
        var recallResponse2 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.20}""" };

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

        // Should have retry status (retry happened)
        events.Should().Contain(e => e.Type == "status" && e.Text == "Improving answer with deeper search...");

        // Quality event shows the retry scores (still low)
        var qualityEvent = events.First(e => e.Type == "quality");
        qualityEvent.Faithfulness.Should().Be(0.25);
        qualityEvent.ContextRecall.Should().Be(0.20);
        qualityEvent.Warning.Should().Be("This answer may not be fully grounded in the knowledge base");

        // Answer is from retry draft
        var chunkText = string.Join("", events.Where(e => e.Type == "chunk").Select(e => e.Text));
        chunkText.Should().Contain("Still poor answer");

        // Only 1 retry: no second "Improving" status
        var improvingStatuses = events.Count(e => e.Type == "status" && e.Text == "Improving answer with deeper search...");
        improvingStatuses.Should().Be(1);

        events.Last().Type.Should().Be("done");
    }

    [Fact]
    public async Task RetryUsesDeepSearchPrompt()
    {
        // Verify retry messages include the instruction for top_k=15
        var searchToolCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test","top_k":5}""" }
            }
        };
        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "answer" };
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "Bad draft" };
        var faithResponse1 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.30}""" };
        var recallResponse1 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.30}""" };

        // Track messages sent to retry agent loop
        List<ChatMessage>? retryMessages = null;
        var callCount = 0;

        _mockLlm.Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .Returns<List<ChatMessage>, List<ToolDefinition>, float>((msgs, tools, temp) =>
            {
                callCount++;
                if (callCount == 6)
                {
                    // Retry agent loop call — capture messages
                    retryMessages = new List<ChatMessage>(msgs);
                    return Task.FromResult(new LlmToolResponse { HasToolCall = false, Content = "retry answer" });
                }

                return callCount switch
                {
                    1 => Task.FromResult(searchToolCall),
                    2 => Task.FromResult(answerResponse),
                    3 => Task.FromResult(draftResponse),
                    4 => Task.FromResult(faithResponse1),
                    5 => Task.FromResult(recallResponse1),
                    7 => Task.FromResult(new LlmToolResponse { HasToolCall = false, Content = "Retry draft" }),
                    8 => Task.FromResult(new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.80}""" }),
                    _ => Task.FromResult(new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.80}""" })
                };
            });

        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Content", Metadata = new() { ["source"] = "doc.pdf" }, Score = 0.9 }
            });

        var service = CreateService();
        await CollectEvents(service.ProcessQueryAsync("test", new List<ChatMessage>()));

        // Verify retry messages contain the deep search instruction
        retryMessages.Should().NotBeNull();
        retryMessages!.Should().Contain(m =>
            m.Role == "system" &&
            m.Content != null &&
            m.Content.Contains("top_k=15"));
    }

    [Fact]
    public async Task RetryAddsSources_FromDeeperSearch()
    {
        // Sources from retry search should be added to existing sources
        var searchToolCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test","top_k":5}""" }
            }
        };
        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "answer" };
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "Poor draft" };
        var faithResponse1 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.40}""" };
        var recallResponse1 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.40}""" };

        var retrySearchCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_r1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test broader","top_k":15}""" }
            }
        };
        var retryAnswerResponse = new LlmToolResponse { HasToolCall = false, Content = "improved" };
        var retryDraftResponse = new LlmToolResponse { HasToolCall = false, Content = "Improved answer" };
        var faithResponse2 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.85}""" };
        var recallResponse2 = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.85}""" };

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

        // First search returns source_a.pdf
        _mockPinecone.Setup(p => p.SimilaritySearchAsync("test", It.IsAny<int>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Content A", Metadata = new() { ["source"] = "source_a.pdf" }, Score = 0.9 }
            });

        // Retry search returns source_b.pdf
        _mockPinecone.Setup(p => p.SimilaritySearchAsync("test broader", It.IsAny<int>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Content B", Metadata = new() { ["source"] = "source_b.pdf" }, Score = 0.95 }
            });

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("test", new List<ChatMessage>()));

        var sources = events.First(e => e.Type == "sources");
        sources.Sources.Should().Contain("source_a.pdf");
        sources.Sources.Should().Contain("source_b.pdf");
    }

    [Fact]
    public void SplitForStreaming_SplitsTextIntoChunks()
    {
        var text = "This is a test sentence that should be split into multiple chunks for streaming.";
        var chunks = AgenticRagPipelineService.SplitForStreaming(text).ToList();

        chunks.Should().HaveCountGreaterThan(1);

        // Recombined text should match original (with space normalization)
        var recombined = string.Join(" ", chunks.Select(c => c.Trim()));
        // Normalize whitespace for comparison
        var normalizedOriginal = string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var normalizedRecombined = string.Join(" ", recombined.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        normalizedRecombined.Should().Be(normalizedOriginal);
    }

    [Fact]
    public void SplitForStreaming_EmptyText_YieldsNothing()
    {
        var chunks = AgenticRagPipelineService.SplitForStreaming("").ToList();
        chunks.Should().BeEmpty();
    }

    [Fact]
    public void SplitForStreaming_ShortText_YieldsSingleChunk()
    {
        var chunks = AgenticRagPipelineService.SplitForStreaming("Hello").ToList();
        chunks.Should().HaveCount(1);
        chunks[0].Should().Be("Hello");
    }

    [Fact]
    public async Task BothScoresExactlyAtThreshold_Passes()
    {
        // Scores exactly at 0.7 should pass
        var searchToolCall = new LlmToolResponse
        {
            HasToolCall = true,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"test","top_k":5}""" }
            }
        };
        var answerResponse = new LlmToolResponse { HasToolCall = false, Content = "answer" };
        var draftResponse = new LlmToolResponse { HasToolCall = false, Content = "Exactly threshold answer" };
        var faithResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.70}""" };
        var recallResponse = new LlmToolResponse { HasToolCall = false, Content = """{"score": 0.70}""" };

        _mockLlm.SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(searchToolCall)
            .ReturnsAsync(answerResponse)
            .ReturnsAsync(draftResponse)
            .ReturnsAsync(faithResponse)
            .ReturnsAsync(recallResponse);

        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Content", Metadata = new() { ["source"] = "doc.pdf" }, Score = 0.9 }
            });

        var service = CreateService();
        var events = await CollectEvents(service.ProcessQueryAsync("test", new List<ChatMessage>()));

        // No retry
        events.Should().NotContain(e => e.Type == "status" && e.Text == "Improving answer with deeper search...");

        // Only 5 calls (no retry)
        _mockLlm.Verify(l => l.ChatWithToolsAsync(
            It.IsAny<List<ChatMessage>>(),
            It.IsAny<List<ToolDefinition>>(),
            It.IsAny<float>()), Times.Exactly(5));

        events.Last().Type.Should().Be("done");
    }
}
