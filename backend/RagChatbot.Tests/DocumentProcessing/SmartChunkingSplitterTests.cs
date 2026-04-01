using FluentAssertions;
using Moq;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.DocumentProcessing;

namespace RagChatbot.Tests.DocumentProcessing;

public class SmartChunkingSplitterTests
{
    private readonly Mock<ILlmService> _llmService;
    private readonly RecursiveCharacterSplitter _fallbackSplitter;
    private readonly SmartChunkingSplitter _splitter;

    public SmartChunkingSplitterTests()
    {
        _llmService = new Mock<ILlmService>();
        _fallbackSplitter = new RecursiveCharacterSplitter(1000, 100);
        _splitter = new SmartChunkingSplitter(_llmService.Object, _fallbackSplitter);
    }

    [Fact]
    public void Split_ValidLlmJsonResponse_ReturnsDocumentChunks()
    {
        // Arrange
        var document = new Document
        {
            PageContent = "Chunk one content. Chunk two content.",
            Metadata = new Dictionary<string, string> { ["source"] = "test.txt" }
        };
        _llmService
            .Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse { Content = "[\"chunk one\", \"chunk two\"]" });

        // Act
        var chunks = _splitter.Split(document);

        // Assert
        chunks.Should().HaveCount(2);
        chunks[0].Content.Should().Be("chunk one");
        chunks[1].Content.Should().Be("chunk two");
        chunks.Should().AllSatisfy(c => c.Source.Should().Be("test.txt"));
        chunks.Should().AllSatisfy(c => c.Id.Should().MatchRegex(@"^doc_\d+_\d+$"));
    }

    [Fact]
    public void Split_InvalidJsonResponse_FallsBackToRecursiveSplitter()
    {
        // Arrange
        var document = new Document
        {
            PageContent = "Some content that should be split by fallback.",
            Metadata = new Dictionary<string, string> { ["source"] = "test.txt" }
        };
        _llmService
            .Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse { Content = "This is not JSON" });

        // Act
        var chunks = _splitter.Split(document);

        // Assert — fallback should still produce chunks
        chunks.Should().NotBeEmpty();
        chunks.Should().AllSatisfy(c => c.Source.Should().Be("test.txt"));
    }

    [Fact]
    public void Split_EmptyArrayResponse_FallsBackToRecursiveSplitter()
    {
        // Arrange
        var document = new Document
        {
            PageContent = "Some content for fallback splitting.",
            Metadata = new Dictionary<string, string> { ["source"] = "test.txt" }
        };
        _llmService
            .Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse { Content = "[]" });

        // Act
        var chunks = _splitter.Split(document);

        // Assert — fallback should produce chunks
        chunks.Should().NotBeEmpty();
        chunks.Should().AllSatisfy(c => c.Source.Should().Be("test.txt"));
    }

    [Fact]
    public void Split_LlmThrowsException_FallsBackToRecursiveSplitter()
    {
        // Arrange
        var document = new Document
        {
            PageContent = "Content for fallback after exception.",
            Metadata = new Dictionary<string, string> { ["source"] = "test.txt" }
        };
        _llmService
            .Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var chunks = _splitter.Split(document);

        // Assert — fallback should produce chunks
        chunks.Should().NotBeEmpty();
        chunks.Should().AllSatisfy(c => c.Source.Should().Be("test.txt"));
    }

    [Fact]
    public void Split_ShortDocument_ReturnsSingleChunk()
    {
        // Arrange
        var document = new Document
        {
            PageContent = "Short content here.",
            Metadata = new Dictionary<string, string> { ["source"] = "short.txt" }
        };
        _llmService
            .Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse { Content = "[\"Short content here.\"]" });

        // Act
        var chunks = _splitter.Split(document);

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].Content.Should().Be("Short content here.");
    }

    [Fact]
    public void Split_SourceMetadataPreserved()
    {
        // Arrange
        var document = new Document
        {
            PageContent = "Report content about important findings.",
            Metadata = new Dictionary<string, string> { ["source"] = "report.pdf" }
        };
        _llmService
            .Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse { Content = "[\"Finding one.\", \"Finding two.\"]" });

        // Act
        var chunks = _splitter.Split(document);

        // Assert
        chunks.Should().HaveCount(2);
        chunks.Should().AllSatisfy(c => c.Source.Should().Be("report.pdf"));
    }

    [Fact]
    public void Split_ChunkIdsGenerated_UniqueAndFormatted()
    {
        // Arrange
        var document = new Document
        {
            PageContent = "Content that will be split into three chunks.",
            Metadata = new Dictionary<string, string> { ["source"] = "test.txt" }
        };
        _llmService
            .Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse { Content = "[\"chunk A\", \"chunk B\", \"chunk C\"]" });

        // Act
        var chunks = _splitter.Split(document);

        // Assert
        chunks.Should().HaveCount(3);
        chunks.Should().AllSatisfy(c => c.Id.Should().MatchRegex(@"^doc_\d+_\d+$"));
        chunks.Select(c => c.Id).Should().OnlyHaveUniqueItems();
    }
}
