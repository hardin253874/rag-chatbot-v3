using FluentAssertions;
using Moq;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.DocumentProcessing;

namespace RagChatbot.Tests.DocumentProcessing;

public class QaChunkingSplitterTests
{
    private readonly Mock<ILlmService> _llmService;
    private readonly NlpChunkingSplitter _nlpSplitter;
    private readonly RecursiveCharacterSplitter _fallbackSplitter;
    private readonly QaChunkingSplitter _splitter;

    public QaChunkingSplitterTests()
    {
        _llmService = new Mock<ILlmService>();
        _nlpSplitter = new NlpChunkingSplitter();
        _fallbackSplitter = new RecursiveCharacterSplitter(1000, 100);
        _splitter = new QaChunkingSplitter(_nlpSplitter, _llmService.Object, _fallbackSplitter);
    }

    /// <summary>
    /// Builds a document that the real NlpChunkingSplitter splits into exactly two segments:
    /// two paragraphs, each a single long sentence (~300 chars, above the 200-char minimum),
    /// separated by a blank line. Verified by the segment-count sanity assertions in tests.
    /// </summary>
    private static Document CreateTwoSegmentDocument(string source = "test.txt")
    {
        var paragraphA = string.Join(" ", Enumerable.Repeat("alpha", 50)) + ".";
        var paragraphB = string.Join(" ", Enumerable.Repeat("beta", 60)) + ".";
        return new Document
        {
            PageContent = paragraphA + "\n\n" + paragraphB,
            Metadata = new Dictionary<string, string> { ["source"] = source }
        };
    }

    private static Document CreateSingleSegmentDocument(string source = "test.txt")
    {
        // Under the NLP splitter's 200-char minimum -> exactly one segment.
        return new Document
        {
            PageContent = "Webcoda is a Sydney-based digital agency founded in 2005.",
            Metadata = new Dictionary<string, string> { ["source"] = source }
        };
    }

    [Fact]
    public void Split_ValidQaJsonResponse_ReturnsFormattedChunks()
    {
        // Arrange
        var document = CreateSingleSegmentDocument();
        _llmService
            .Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse { Content = "[{\"question\":\"Q1\",\"answer\":\"A1\"}]" });

        // Act
        var chunks = _splitter.Split(document);

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].Content.Should().Be("Q: Q1\nA: A1");
        chunks[0].Source.Should().Be("test.txt");
        chunks[0].Id.Should().MatchRegex(@"^doc_\d+_\d+$");
    }

    [Fact]
    public void Split_MultipleSegments_AggregatesPairsWithUniqueSequentialIds()
    {
        // Arrange
        var document = CreateTwoSegmentDocument();
        _nlpSplitter.Split(document).Should().HaveCount(2, "the crafted document must pre-chunk into exactly two segments");

        _llmService
            .SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse { Content = "[{\"question\":\"Q1\",\"answer\":\"A1\"}]" })
            .ReturnsAsync(new LlmToolResponse { Content = "[{\"question\":\"Q2\",\"answer\":\"A2\"}]" });

        // Act
        var chunks = _splitter.Split(document);

        // Assert — pairs from both segments flattened, in order
        chunks.Should().HaveCount(2);
        chunks[0].Content.Should().Be("Q: Q1\nA: A1");
        chunks[1].Content.Should().Be("Q: Q2\nA: A2");
        chunks.Select(c => c.Id).Should().OnlyHaveUniqueItems();
        chunks[0].Id.Should().EndWith("_0");
        chunks[1].Id.Should().EndWith("_1");
    }

    [Fact]
    public void Split_EmptyArraySegment_ContributesNoChunks()
    {
        // Arrange — first segment is boilerplate ([]), second yields a valid pair
        var document = CreateTwoSegmentDocument();
        _nlpSplitter.Split(document).Should().HaveCount(2, "the crafted document must pre-chunk into exactly two segments");

        _llmService
            .SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse { Content = "[]" })
            .ReturnsAsync(new LlmToolResponse { Content = "[{\"question\":\"Q2\",\"answer\":\"A2\"}]" });

        // Act
        var chunks = _splitter.Split(document);

        // Assert — only the valid segment's pair survives
        chunks.Should().HaveCount(1);
        chunks[0].Content.Should().Be("Q: Q2\nA: A2");
    }

    [Fact]
    public void Split_InvalidJsonSegment_SoftSkipsAndKeepsValidPairs()
    {
        // Arrange — first segment returns garbage, second returns valid JSON
        var document = CreateTwoSegmentDocument();
        _nlpSplitter.Split(document).Should().HaveCount(2, "the crafted document must pre-chunk into exactly two segments");

        _llmService
            .SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse { Content = "not json" })
            .ReturnsAsync(new LlmToolResponse { Content = "[{\"question\":\"Q2\",\"answer\":\"A2\"}]" });

        // Act
        var chunks = _splitter.Split(document);

        // Assert — invalid segment skipped, valid one survives
        chunks.Should().HaveCount(1);
        chunks[0].Content.Should().Be("Q: Q2\nA: A2");
    }

    [Fact]
    public void Split_AllLlmCallsThrow_FallsBackToRecursiveSplitter()
    {
        // Arrange
        var document = CreateTwoSegmentDocument("report.pdf");
        _llmService
            .Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var chunks = _splitter.Split(document);

        // Assert — fallback should still produce chunks
        chunks.Should().NotBeEmpty();
        chunks.Should().AllSatisfy(c => c.Source.Should().Be("report.pdf"));
    }

    [Fact]
    public void Split_FencedJsonResponse_IsTolerated()
    {
        // Arrange
        var document = CreateSingleSegmentDocument();
        _llmService
            .Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse
            {
                Content = "```json\n[{\"question\":\"Q1\",\"answer\":\"A1\"}]\n```"
            });

        // Act
        var chunks = _splitter.Split(document);

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].Content.Should().Be("Q: Q1\nA: A1");
    }

    [Fact]
    public void Split_BlankQuestionOrAnswer_PairIsDropped()
    {
        // Arrange — one pair has an empty answer, one has a blank question, one is valid
        var document = CreateSingleSegmentDocument();
        _llmService
            .Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse
            {
                Content = "[{\"question\":\"Q1\",\"answer\":\"\"}," +
                          "{\"question\":\"   \",\"answer\":\"A2\"}," +
                          "{\"question\":\"Q3\",\"answer\":\"A3\"}]"
            });

        // Act
        var chunks = _splitter.Split(document);

        // Assert — only the complete pair survives
        chunks.Should().HaveCount(1);
        chunks[0].Content.Should().Be("Q: Q3\nA: A3");
    }

    [Fact]
    public void Split_ChunkIdsGenerated_UniqueAndFormatted()
    {
        // Arrange
        var document = CreateTwoSegmentDocument();
        _nlpSplitter.Split(document).Should().HaveCount(2, "the crafted document must pre-chunk into exactly two segments");

        _llmService
            .SetupSequence(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse
            {
                Content = "[{\"question\":\"Q1\",\"answer\":\"A1\"},{\"question\":\"Q2\",\"answer\":\"A2\"}]"
            })
            .ReturnsAsync(new LlmToolResponse
            {
                Content = "[{\"question\":\"Q3\",\"answer\":\"A3\"}]"
            });

        // Act
        var chunks = _splitter.Split(document);

        // Assert
        chunks.Should().HaveCount(3);
        chunks.Should().AllSatisfy(c => c.Id.Should().MatchRegex(@"^doc_\d+_\d+$"));
        chunks.Select(c => c.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Split_NullDocument_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _splitter.Split(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
