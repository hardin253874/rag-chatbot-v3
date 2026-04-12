using FluentAssertions;
using Moq;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.Chat.Tools;

namespace RagChatbot.Tests.Chat.Tools;

public class SearchKnowledgeBaseToolTests
{
    private readonly Mock<IPineconeService> _mockPinecone = new();

    private SearchKnowledgeBaseTool CreateTool() => new(_mockPinecone.Object);

    [Fact]
    public async Task ExecuteAsync_ParsesArgsAndFormatsResults()
    {
        // topK=3 -> fetchK = min(6, 20) = 6, but we return 2 results which is less than topK
        _mockPinecone.Setup(p => p.SimilaritySearchAsync("test query", 6, It.IsAny<string?>()))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "First chunk", Metadata = new() { ["source"] = "doc1.pdf" }, Score = 0.87 },
                new() { PageContent = "Second chunk", Metadata = new() { ["source"] = "notes.md" }, Score = 0.82 }
            });

        var tool = CreateTool();
        var result = await tool.ExecuteAsync("""{"query":"test query","top_k":3}""");

        result.Should().Contain("Found 2 results");
        result.Should().Contain("[1] (score: 0.87, source: doc1.pdf)");
        result.Should().Contain("First chunk");
        result.Should().Contain("[2] (score: 0.82, source: notes.md)");
        result.Should().Contain("Second chunk");
    }

    [Fact]
    public async Task ExecuteAsync_DefaultsTopKTo8_OverFetchesTo16()
    {
        // Default topK=8 -> fetchK = min(16, 20) = 16
        _mockPinecone.Setup(p => p.SimilaritySearchAsync("test", 16, It.IsAny<string?>()))
            .ReturnsAsync(new List<Document>());

        var tool = CreateTool();
        await tool.ExecuteAsync("""{"query":"test"}""");

        _mockPinecone.Verify(p => p.SimilaritySearchAsync("test", 16, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ClampsTopKToMax20()
    {
        // topK=50 -> clamped to 20, fetchK = min(40, 20) = 20
        _mockPinecone.Setup(p => p.SimilaritySearchAsync("test", 20, It.IsAny<string?>()))
            .ReturnsAsync(new List<Document>());

        var tool = CreateTool();
        await tool.ExecuteAsync("""{"query":"test","top_k":50}""");

        _mockPinecone.Verify(p => p.SimilaritySearchAsync("test", 20, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsEmptyMessage_WhenNoResults()
    {
        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(new List<Document>());

        var tool = CreateTool();
        var result = await tool.ExecuteAsync("""{"query":"nothing"}""");

        result.Should().Contain("Found 0 results");
    }

    // --- B21: CurrentProjectFilter tests ---

    [Fact]
    public async Task ExecuteAsync_WithCurrentProjectFilter_PassesFilterToSearch()
    {
        _mockPinecone.Setup(p => p.SimilaritySearchAsync("test query", It.IsAny<int>(), "NESA"))
            .ReturnsAsync(new List<Document>
            {
                new() { PageContent = "Result", Metadata = new() { ["source"] = "doc.pdf" }, Score = 0.9 }
            });

        var tool = CreateTool();
        tool.CurrentProjectFilter = "NESA";
        await tool.ExecuteAsync("""{"query":"test query"}""");

        _mockPinecone.Verify(p => p.SimilaritySearchAsync("test query", It.IsAny<int>(), "NESA"), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutProjectFilter_PassesNullFilter()
    {
        _mockPinecone.Setup(p => p.SimilaritySearchAsync("test query", It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(new List<Document>());

        var tool = CreateTool();
        // CurrentProjectFilter is null by default
        await tool.ExecuteAsync("""{"query":"test query"}""");

        _mockPinecone.Verify(p => p.SimilaritySearchAsync("test query", It.IsAny<int>(), null), Times.Once);
    }

    [Fact]
    public void Definition_MatchesExpectedSchema()
    {
        var tool = CreateTool();

        tool.Name.Should().Be("search_knowledge_base");
        tool.Definition.Name.Should().Be("search_knowledge_base");
        tool.Definition.Description.Should().Contain("Search the knowledge base");
    }
}
