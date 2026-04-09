using FluentAssertions;
using Moq;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.DocumentProcessing;

namespace RagChatbot.Tests.DocumentProcessing;

public class HybridChunkingSplitterTests
{
    private readonly NlpChunkingSplitter _nlpSplitter;
    private readonly Mock<ILlmService> _llmService;
    private readonly RecursiveCharacterSplitter _fallbackSplitter;
    private readonly HybridChunkingSplitter _splitter;

    public HybridChunkingSplitterTests()
    {
        _nlpSplitter = new NlpChunkingSplitter();
        _llmService = new Mock<ILlmService>();
        _fallbackSplitter = new RecursiveCharacterSplitter(1000, 100);
        _splitter = new HybridChunkingSplitter(_nlpSplitter, _llmService.Object, _fallbackSplitter);
    }

    private static Document CreateMultiParagraphDocument(string source = "report.md")
    {
        // Create a document with enough text for NLP splitting (400+ chars with paragraph breaks)
        var text = """
            # Introduction

            This is the first paragraph of the document. It discusses the fundamental concepts
            of retrieval-augmented generation and how it improves answer quality. The approach
            combines vector search with large language model capabilities to provide grounded responses.

            # Architecture Overview

            The system architecture consists of multiple components working together. The ingestion
            pipeline processes documents through chunking and embedding stages. The query pipeline
            retrieves relevant chunks and feeds them to the language model for answer generation.
            """;
        return new Document
        {
            PageContent = text,
            Metadata = new Dictionary<string, string> { ["source"] = source }
        };
    }

    [Fact]
    public void Split_ValidLlmJsonResponse_ReturnsRefinedChunks()
    {
        // Arrange
        var document = CreateMultiParagraphDocument("report.md");
        _llmService
            .Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse
            {
                Content = "[\"refined chunk one\", \"refined chunk two\"]"
            });

        // Act
        var chunks = _splitter.Split(document);

        // Assert
        chunks.Should().HaveCount(2);
        chunks[0].Content.Should().Be("refined chunk one");
        chunks[1].Content.Should().Be("refined chunk two");
        chunks.Should().AllSatisfy(c => c.Source.Should().Be("report.md"));
        chunks.Should().AllSatisfy(c => c.Id.Should().MatchRegex(@"^doc_\d+_\d+$"));
    }

    [Fact]
    public void Split_InvalidJsonResponse_FallsBackToNlpSegments()
    {
        // Arrange
        var document = CreateMultiParagraphDocument();
        _llmService
            .Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse { Content = "Not valid JSON at all" });

        // Act
        var chunks = _splitter.Split(document);

        // Assert - fallback produces NLP chunks
        chunks.Should().NotBeEmpty();
        chunks.Should().AllSatisfy(c => c.Source.Should().Be("report.md"));
    }

    [Fact]
    public void Split_EmptyArrayResponse_FallsBackToNlpSegments()
    {
        // Arrange
        var document = CreateMultiParagraphDocument();
        _llmService
            .Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse { Content = "[]" });

        // Act
        var chunks = _splitter.Split(document);

        // Assert
        chunks.Should().NotBeEmpty();
    }

    [Fact]
    public void Split_LlmThrowsException_FallsBackToNlpSegments()
    {
        // Arrange
        var document = CreateMultiParagraphDocument();
        _llmService
            .Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var chunks = _splitter.Split(document);

        // Assert - no exception propagated, returns NLP segments
        chunks.Should().NotBeEmpty();
        chunks.Should().AllSatisfy(c => c.Source.Should().Be("report.md"));
    }

    [Fact]
    public void Split_WithProgressCallback_ReportsStages()
    {
        // Arrange
        var document = CreateMultiParagraphDocument();
        var progressMessages = new List<string>();
        _llmService
            .Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse
            {
                Content = "[\"refined chunk one\", \"refined chunk two\"]"
            });

        _splitter.WithProgress(msg => progressMessages.Add(msg));

        // Act
        _splitter.Split(document);

        // Assert
        progressMessages.Should().Contain(m => m.Contains("NLP pre-chunking"));
        progressMessages.Should().Contain(m => m.Contains("LLM refining"));
        progressMessages.Should().Contain(m => m.Contains("Refined into"));
    }

    [Fact]
    public void Split_SendsNlpSegmentsInPrompt()
    {
        // Arrange
        var document = CreateMultiParagraphDocument();
        List<ChatMessage>? capturedMessages = null;

        _llmService
            .Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .Callback<List<ChatMessage>, List<ToolDefinition>, float>((msgs, _, _) =>
            {
                capturedMessages = msgs;
            })
            .ReturnsAsync(new LlmToolResponse
            {
                Content = "[\"chunk one\"]"
            });

        // Act
        _splitter.Split(document);

        // Assert
        capturedMessages.Should().NotBeNull();
        var prompt = capturedMessages![0].Content;
        prompt.Should().Contain("[1]");
        prompt.Should().Contain("[2]");
    }

    [Fact]
    public void Split_EmptyLlmContent_FallsBackToNlpSegments()
    {
        // Arrange
        var document = CreateMultiParagraphDocument();
        _llmService
            .Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse { Content = null });

        // Act
        var chunks = _splitter.Split(document);

        // Assert
        chunks.Should().NotBeEmpty();
    }

    [Fact]
    public void Split_CallsLlmWithTemperatureZero()
    {
        // Arrange
        var document = CreateMultiParagraphDocument();
        float capturedTemp = -1;

        _llmService
            .Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .Callback<List<ChatMessage>, List<ToolDefinition>, float>((_, _, temp) =>
            {
                capturedTemp = temp;
            })
            .ReturnsAsync(new LlmToolResponse
            {
                Content = "[\"chunk\"]"
            });

        // Act
        _splitter.Split(document);

        // Assert
        capturedTemp.Should().Be(0.0f);
    }

    [Fact]
    public void Split_CallsLlmWithEmptyToolsList()
    {
        // Arrange
        var document = CreateMultiParagraphDocument();
        List<ToolDefinition>? capturedTools = null;

        _llmService
            .Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .Callback<List<ChatMessage>, List<ToolDefinition>, float>((_, tools, _) =>
            {
                capturedTools = tools;
            })
            .ReturnsAsync(new LlmToolResponse
            {
                Content = "[\"chunk\"]"
            });

        // Act
        _splitter.Split(document);

        // Assert
        capturedTools.Should().NotBeNull();
        capturedTools.Should().BeEmpty();
    }
}
