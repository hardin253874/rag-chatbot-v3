using FluentAssertions;
using Moq;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.Chat;

namespace RagChatbot.Tests.Chat;

public class RagPipelineServiceTests
{
    private readonly Mock<IConversationalDetector> _mockDetector = new();
    private readonly Mock<IQueryRewriteService> _mockRewriter = new();
    private readonly Mock<IPineconeService> _mockPinecone = new();
    private readonly Mock<ILlmService> _mockLlm = new();

    private RagPipelineService CreateService() => new(
        _mockDetector.Object,
        _mockRewriter.Object,
        _mockPinecone.Object,
        _mockLlm.Object);

    private static async IAsyncEnumerable<string> AsyncTokens(params string[] tokens)
    {
        foreach (var token in tokens)
        {
            yield return token;
            await Task.CompletedTask;
        }
    }

    private async Task<List<SseEvent>> CollectEvents(IAsyncEnumerable<SseEvent> events)
    {
        var result = new List<SseEvent>();
        await foreach (var e in events)
        {
            result.Add(e);
        }
        return result;
    }

    [Fact]
    public async Task ProcessQueryAsync_RagPath_RewritesAndSearchesAndStreams()
    {
        // Arrange
        _mockDetector.Setup(d => d.IsFollowUp(It.IsAny<string>())).Returns(false);
        _mockRewriter.Setup(r => r.RewriteQueryAsync("What is RAG?")).ReturnsAsync("RAG explanation");
        _mockPinecone.Setup(p => p.SimilaritySearchAsync("RAG explanation", 5))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "RAG is retrieval-augmented generation", Metadata = new() { ["source"] = "doc.md" } },
                new() { PageContent = "It combines search with LLM", Metadata = new() { ["source"] = "guide.txt" } }
            });
        _mockLlm.Setup(l => l.StreamCompletionAsync(It.IsAny<List<ChatMessage>>(), 0.2f))
            .Returns(AsyncTokens("RAG ", "is great"));

        var service = CreateService();

        // Act
        var events = await CollectEvents(service.ProcessQueryAsync("What is RAG?", new List<ChatMessage>()));

        // Assert
        events.Should().HaveCount(4); // 2 chunks + sources + done

        events[0].Type.Should().Be("chunk");
        events[0].Text.Should().Be("RAG ");

        events[1].Type.Should().Be("chunk");
        events[1].Text.Should().Be("is great");

        events[2].Type.Should().Be("sources");
        events[2].Sources.Should().BeEquivalentTo(new[] { "doc.md", "guide.txt" });

        events[3].Type.Should().Be("done");
    }

    [Fact]
    public async Task ProcessQueryAsync_RagPath_UsesOriginalQuestionInLlmPrompt()
    {
        // Arrange
        _mockDetector.Setup(d => d.IsFollowUp(It.IsAny<string>())).Returns(false);
        _mockRewriter.Setup(r => r.RewriteQueryAsync("whats rag?")).ReturnsAsync("RAG explanation");
        _mockPinecone.Setup(p => p.SimilaritySearchAsync("RAG explanation", 5))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "RAG info", Metadata = new() { ["source"] = "s.md" } }
            });

        List<ChatMessage>? capturedMessages = null;
        _mockLlm.Setup(l => l.StreamCompletionAsync(It.IsAny<List<ChatMessage>>(), 0.2f))
            .Callback<List<ChatMessage>, float>((msgs, _) => capturedMessages = msgs)
            .Returns(AsyncTokens("answer"));

        var service = CreateService();

        // Act
        await CollectEvents(service.ProcessQueryAsync("whats rag?", new List<ChatMessage>()));

        // Assert — original question in user message, not rewritten query
        capturedMessages.Should().NotBeNull();
        var userMessage = capturedMessages!.Last();
        userMessage.Content.Should().Contain("whats rag?");
        userMessage.Content.Should().NotContain("RAG explanation");
    }

    [Fact]
    public async Task ProcessQueryAsync_RagPath_NumbersContextChunks()
    {
        // Arrange
        _mockDetector.Setup(d => d.IsFollowUp(It.IsAny<string>())).Returns(false);
        _mockRewriter.Setup(r => r.RewriteQueryAsync(It.IsAny<string>())).ReturnsAsync("query");
        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), 5))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "First chunk", Metadata = new() { ["source"] = "a.md" } },
                new() { PageContent = "Second chunk", Metadata = new() { ["source"] = "b.md" } }
            });

        List<ChatMessage>? capturedMessages = null;
        _mockLlm.Setup(l => l.StreamCompletionAsync(It.IsAny<List<ChatMessage>>(), 0.2f))
            .Callback<List<ChatMessage>, float>((msgs, _) => capturedMessages = msgs)
            .Returns(AsyncTokens("ok"));

        var service = CreateService();

        // Act
        await CollectEvents(service.ProcessQueryAsync("test", new List<ChatMessage>()));

        // Assert — context should have numbered chunks
        var systemMessage = capturedMessages!.First(m => m.Role == "system");
        systemMessage.Content.Should().Contain("[1] First chunk");
        systemMessage.Content.Should().Contain("[2] Second chunk");
    }

    [Fact]
    public async Task ProcessQueryAsync_NoResults_ReturnsNotFoundMessage()
    {
        // Arrange
        _mockDetector.Setup(d => d.IsFollowUp(It.IsAny<string>())).Returns(false);
        _mockRewriter.Setup(r => r.RewriteQueryAsync(It.IsAny<string>())).ReturnsAsync("query");
        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), 5))
            .ReturnsAsync(new List<Document>());

        var service = CreateService();

        // Act
        var events = await CollectEvents(service.ProcessQueryAsync("unknown topic", new List<ChatMessage>()));

        // Assert
        events.Should().HaveCount(2); // chunk + done
        events[0].Type.Should().Be("chunk");
        events[0].Text.Should().Be("I couldn't find any relevant information in the knowledge base.");
        events[1].Type.Should().Be("done");

        // LLM should not be called
        _mockLlm.Verify(l => l.StreamCompletionAsync(It.IsAny<List<ChatMessage>>(), It.IsAny<float>()), Times.Never);
    }

    [Fact]
    public async Task ProcessQueryAsync_ConversationalWithHistory_SkipsVectorSearch()
    {
        // Arrange
        _mockDetector.Setup(d => d.IsFollowUp("Can you repeat that?")).Returns(true);
        _mockLlm.Setup(l => l.StreamCompletionAsync(It.IsAny<List<ChatMessage>>(), 0.2f))
            .Returns(AsyncTokens("Sure, I said..."));

        var history = new List<ChatMessage>
        {
            new() { Role = "user", Content = "What is RAG?" },
            new() { Role = "assistant", Content = "RAG is..." }
        };

        var service = CreateService();

        // Act
        var events = await CollectEvents(service.ProcessQueryAsync("Can you repeat that?", history));

        // Assert
        events.Should().HaveCount(2); // chunk + done (no sources for conversational)
        events[0].Type.Should().Be("chunk");
        events[0].Text.Should().Be("Sure, I said...");
        events[1].Type.Should().Be("done");

        // Vector search and rewrite should NOT be called
        _mockRewriter.Verify(r => r.RewriteQueryAsync(It.IsAny<string>()), Times.Never);
        _mockPinecone.Verify(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ProcessQueryAsync_ConversationalWithNoHistory_FallsBackToRag()
    {
        // Arrange — conversational detected but no history, so fall back to RAG path
        _mockDetector.Setup(d => d.IsFollowUp("summarise")).Returns(true);
        _mockRewriter.Setup(r => r.RewriteQueryAsync("summarise")).ReturnsAsync("summarise");
        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), 5))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Some content", Metadata = new() { ["source"] = "file.md" } }
            });
        _mockLlm.Setup(l => l.StreamCompletionAsync(It.IsAny<List<ChatMessage>>(), 0.2f))
            .Returns(AsyncTokens("answer"));

        var service = CreateService();

        // Act
        var events = await CollectEvents(service.ProcessQueryAsync("summarise", new List<ChatMessage>()));

        // Assert — should go through RAG path since history is empty
        _mockRewriter.Verify(r => r.RewriteQueryAsync("summarise"), Times.Once);
        _mockPinecone.Verify(p => p.SimilaritySearchAsync(It.IsAny<string>(), 5), Times.Once);
    }

    [Fact]
    public async Task ProcessQueryAsync_DeduplicatesSources()
    {
        // Arrange
        _mockDetector.Setup(d => d.IsFollowUp(It.IsAny<string>())).Returns(false);
        _mockRewriter.Setup(r => r.RewriteQueryAsync(It.IsAny<string>())).ReturnsAsync("query");
        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), 5))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Chunk 1", Metadata = new() { ["source"] = "same.md" } },
                new() { PageContent = "Chunk 2", Metadata = new() { ["source"] = "same.md" } },
                new() { PageContent = "Chunk 3", Metadata = new() { ["source"] = "other.txt" } }
            });
        _mockLlm.Setup(l => l.StreamCompletionAsync(It.IsAny<List<ChatMessage>>(), 0.2f))
            .Returns(AsyncTokens("answer"));

        var service = CreateService();

        // Act
        var events = await CollectEvents(service.ProcessQueryAsync("test", new List<ChatMessage>()));

        // Assert — sources should be deduplicated
        var sourcesEvent = events.First(e => e.Type == "sources");
        sourcesEvent.Sources.Should().BeEquivalentTo(new[] { "same.md", "other.txt" });
    }

    [Fact]
    public async Task ProcessQueryAsync_IncludesConversationHistoryInRagPrompt()
    {
        // Arrange
        _mockDetector.Setup(d => d.IsFollowUp(It.IsAny<string>())).Returns(false);
        _mockRewriter.Setup(r => r.RewriteQueryAsync(It.IsAny<string>())).ReturnsAsync("query");
        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), 5))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Content", Metadata = new() { ["source"] = "x.md" } }
            });

        List<ChatMessage>? capturedMessages = null;
        _mockLlm.Setup(l => l.StreamCompletionAsync(It.IsAny<List<ChatMessage>>(), 0.2f))
            .Callback<List<ChatMessage>, float>((msgs, _) => capturedMessages = msgs)
            .Returns(AsyncTokens("answer"));

        var history = new List<ChatMessage>
        {
            new() { Role = "user", Content = "Previous Q" },
            new() { Role = "assistant", Content = "Previous A" }
        };

        var service = CreateService();

        // Act
        await CollectEvents(service.ProcessQueryAsync("new question", history));

        // Assert — history should appear in the system message context
        var systemMessage = capturedMessages!.First(m => m.Role == "system");
        systemMessage.Content.Should().Contain("Previous Q");
        systemMessage.Content.Should().Contain("Previous A");
    }

    [Fact]
    public async Task ProcessQueryAsync_AlwaysEndsWithDone()
    {
        // Arrange
        _mockDetector.Setup(d => d.IsFollowUp(It.IsAny<string>())).Returns(false);
        _mockRewriter.Setup(r => r.RewriteQueryAsync(It.IsAny<string>())).ReturnsAsync("q");
        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), 5))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "C", Metadata = new() { ["source"] = "s" } }
            });
        _mockLlm.Setup(l => l.StreamCompletionAsync(It.IsAny<List<ChatMessage>>(), 0.2f))
            .Returns(AsyncTokens("text"));

        var service = CreateService();

        // Act
        var events = await CollectEvents(service.ProcessQueryAsync("q", new List<ChatMessage>()));

        // Assert
        events.Last().Type.Should().Be("done");
    }
}
