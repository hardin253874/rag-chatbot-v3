using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Moq;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.DocumentProcessing;
using RagChatbot.Infrastructure.Ingestion;
using UglyToad.PdfPig.Writer;

// Disambiguate: this test file references both our domain model `Document` and OpenXml's.
using Document = RagChatbot.Core.Models.Document;
using OpenXmlDocument = DocumentFormat.OpenXml.Wordprocessing.Document;

namespace RagChatbot.Tests.Ingestion;

public class IngestionServiceTests
{
    private readonly Mock<IDocumentLoader> _documentLoader;
    private readonly Mock<IUrlLoader> _urlLoader;
    private readonly Mock<IPineconeService> _pineconeService;
    private readonly RecursiveCharacterSplitter _fixedSplitter;
    private readonly NlpChunkingSplitter _nlpSplitter;
    private readonly Mock<ILlmService> _mockLlm;
    private readonly SmartChunkingSplitter _smartSplitter;
    private readonly HybridChunkingSplitter _hybridSplitter;
    private readonly IngestionService _service;

    public IngestionServiceTests()
    {
        _documentLoader = new Mock<IDocumentLoader>();
        _urlLoader = new Mock<IUrlLoader>();
        _pineconeService = new Mock<IPineconeService>();
        _fixedSplitter = new RecursiveCharacterSplitter(1000, 100);
        _nlpSplitter = new NlpChunkingSplitter();
        _mockLlm = new Mock<ILlmService>();
        _smartSplitter = new SmartChunkingSplitter(_mockLlm.Object, _fixedSplitter);
        _hybridSplitter = new HybridChunkingSplitter(_nlpSplitter, _mockLlm.Object, _fixedSplitter);

        _service = new IngestionService(
            _documentLoader.Object,
            _urlLoader.Object,
            _pineconeService.Object,
            _fixedSplitter,
            _nlpSplitter,
            _smartSplitter,
            _hybridSplitter);
    }

    /// <summary>
    /// Helper to collect all events from an IAsyncEnumerable into a list.
    /// </summary>
    private static async Task<List<IngestSseEvent>> CollectEventsAsync(IAsyncEnumerable<IngestSseEvent> events)
    {
        var result = new List<IngestSseEvent>();
        await foreach (var evt in events)
        {
            result.Add(evt);
        }
        return result;
    }

    private void SetupPineconeStore()
    {
        _pineconeService.Setup(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()))
            .Returns(Task.CompletedTask);
    }

    private Document CreateDocument(string content, string source)
    {
        return new Document
        {
            PageContent = content,
            Metadata = new Dictionary<string, string> { ["source"] = source }
        };
    }

    // --- IngestFileStreamAsync Tests ---

    [Fact]
    public async Task IngestFileStreamAsync_MdFile_EmitsDoneEvent()
    {
        // Arrange
        var document = CreateDocument("# Heading\nSome content", "test.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "test.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("# Heading\nSome content"u8.ToArray());

        // Act
        var events = await CollectEventsAsync(
            _service.IngestFileStreamAsync(stream, "test.md"));

        // Assert
        events.Should().Contain(e => e.Type == "done");
        var doneEvent = events.Last(e => e.Type == "done");
        doneEvent.Message.Should().Contain("test.md");
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.All(c => c.Source == "test.md"))), Times.Once);
    }

    [Fact]
    public async Task IngestFileStreamAsync_TxtFile_EmitsDoneEvent()
    {
        // Arrange
        var document = CreateDocument("Some plain text content for testing", "notes.txt");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "notes.txt"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("Some plain text content for testing"u8.ToArray());

        // Act
        var events = await CollectEventsAsync(
            _service.IngestFileStreamAsync(stream, "notes.txt"));

        // Assert
        var doneEvent = events.Last(e => e.Type == "done");
        doneEvent.Message.Should().Contain("notes.txt");
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.All(c => c.Source == "notes.txt"))), Times.Once);
    }

    [Fact]
    public async Task IngestFileStreamAsync_StoresOriginalFilename_NotTempPath()
    {
        // Arrange
        var document = CreateDocument("Content", "report.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "report.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("Content"u8.ToArray());

        // Act
        await CollectEventsAsync(_service.IngestFileStreamAsync(stream, "report.md"));

        // Assert
        _documentLoader.Verify(l => l.LoadAsync(It.IsAny<string>(), "report.md"), Times.Once);
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.All(c => c.Source == "report.md"))), Times.Once);
    }

    [Fact]
    public async Task IngestFileStreamAsync_CallsPineconeStoreDocuments()
    {
        // Arrange
        var document = CreateDocument("Content here", "data.txt");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "data.txt"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("Content here"u8.ToArray());

        // Act
        await CollectEventsAsync(_service.IngestFileStreamAsync(stream, "data.txt"));

        // Assert
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.Count > 0)), Times.Once);
    }

    [Fact]
    public async Task IngestFileStreamAsync_ReturnsStatusAndDoneEvents()
    {
        // Arrange
        var document = CreateDocument("Content", "readme.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "readme.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("Content"u8.ToArray());

        // Act
        var events = await CollectEventsAsync(
            _service.IngestFileStreamAsync(stream, "readme.md"));

        // Assert
        events.Should().Contain(e => e.Type == "status");
        var lastEvent = events.Last();
        lastEvent.Type.Should().Be("done");
        lastEvent.Chunks.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task IngestFileStreamAsync_CleansUpTempFile()
    {
        // Arrange
        var document = CreateDocument("Content", "test.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "test.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("Content"u8.ToArray());

        // Act
        await CollectEventsAsync(_service.IngestFileStreamAsync(stream, "test.md"));

        // Assert
        _documentLoader.Verify(l => l.LoadAsync(It.Is<string>(path =>
            !File.Exists(path) || path == string.Empty), "test.md"), Times.Once);
    }

    [Fact]
    public async Task IngestFileStreamAsync_LoaderThrows_EmitsErrorEvent()
    {
        // Arrange
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "bad.md"))
            .ThrowsAsync(new InvalidOperationException("Load failed"));

        using var stream = new MemoryStream("Content"u8.ToArray());

        // Act
        var events = await CollectEventsAsync(
            _service.IngestFileStreamAsync(stream, "bad.md"));

        // Assert
        var lastEvent = events.Last();
        lastEvent.Type.Should().Be("error");
        lastEvent.Message.Should().Contain("Load failed");
    }

    // --- IngestUrlStreamAsync Tests ---

    [Fact]
    public async Task IngestUrlStreamAsync_ReturnsStatusAndDoneEvents()
    {
        // Arrange
        var document = CreateDocument("Web page content about RAG chatbots", "https://example.com/article");
        _urlLoader.Setup(l => l.LoadAsync("https://example.com/article"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        // Act
        var events = await CollectEventsAsync(
            _service.IngestUrlStreamAsync("https://example.com/article"));

        // Assert
        events.Should().Contain(e => e.Type == "status");
        var doneEvent = events.Last();
        doneEvent.Type.Should().Be("done");
        doneEvent.Message.Should().Contain("https://example.com/article");
        doneEvent.Chunks.Should().BeGreaterThan(0);
        _urlLoader.Verify(l => l.LoadAsync("https://example.com/article"), Times.Once);
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.Count > 0 && chunks.All(c => c.Source == "https://example.com/article"))),
            Times.Once);
    }

    [Fact]
    public async Task IngestUrlStreamAsync_EmitsDoneEventWithChunkCount()
    {
        // Arrange
        var document = CreateDocument("Content", "https://example.com");
        _urlLoader.Setup(l => l.LoadAsync("https://example.com"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        // Act
        var events = await CollectEventsAsync(
            _service.IngestUrlStreamAsync("https://example.com"));

        // Assert
        var doneEvent = events.Last();
        doneEvent.Type.Should().Be("done");
        doneEvent.Chunks.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task IngestFileStreamAsync_UsesInjectedTextSplitter()
    {
        // Arrange -- default chunkingMode is "nlp", so NlpChunkingSplitter should be used
        var document = CreateDocument("File content to split", "data.txt");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "data.txt"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("File content to split"u8.ToArray());

        // Act
        await CollectEventsAsync(_service.IngestFileStreamAsync(stream, "data.txt"));

        // Assert -- NlpChunkingSplitter should produce chunks (short doc = 1 chunk)
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.Count > 0 && chunks.All(c => c.Source == "data.txt"))), Times.Once);
    }

    [Fact]
    public async Task IngestUrlStreamAsync_UsesInjectedTextSplitter()
    {
        // Arrange
        var document = CreateDocument("URL content to split", "https://example.com/page");
        _urlLoader.Setup(l => l.LoadAsync("https://example.com/page"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        // Act
        await CollectEventsAsync(_service.IngestUrlStreamAsync("https://example.com/page"));

        // Assert
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.Count > 0 && chunks.All(c => c.Source == "https://example.com/page"))), Times.Once);
    }

    // --- Mode selection tests ---

    [Fact]
    public async Task IngestFileStreamAsync_FixedMode_ProducesChunks()
    {
        // Arrange -- long enough text that RecursiveCharacterSplitter will split
        var longText = string.Join(" ", Enumerable.Range(1, 200).Select(i => $"Word{i}"));
        var document = CreateDocument(longText, "data.txt");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "data.txt"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(longText));

        // Act
        await CollectEventsAsync(_service.IngestFileStreamAsync(stream, "data.txt", "fixed"));

        // Assert -- chunks should be stored
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.Count > 0)), Times.Once);
    }

    [Fact]
    public async Task IngestFileStreamAsync_NlpMode_ProducesChunks()
    {
        // Arrange
        var document = CreateDocument("Short NLP content.", "doc.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "doc.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("Short NLP content."u8.ToArray());

        // Act
        await CollectEventsAsync(_service.IngestFileStreamAsync(stream, "doc.md", "nlp"));

        // Assert
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.Count > 0)), Times.Once);
    }

    [Fact]
    public async Task IngestFileStreamAsync_DefaultMode_UsesNlpSplitter()
    {
        // Arrange -- no chunkingMode param means default = "nlp"
        var document = CreateDocument("Default mode content.", "doc.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "doc.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("Default mode content."u8.ToArray());

        // Act -- no chunkingMode parameter
        await CollectEventsAsync(_service.IngestFileStreamAsync(stream, "doc.md"));

        // Assert
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.Count > 0)), Times.Once);
    }

    [Fact]
    public async Task IngestUrlStreamAsync_FixedMode_ProducesChunks()
    {
        // Arrange
        var document = CreateDocument("URL content for fixed mode", "https://example.com");
        _urlLoader.Setup(l => l.LoadAsync("https://example.com"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        // Act
        await CollectEventsAsync(_service.IngestUrlStreamAsync("https://example.com", "fixed"));

        // Assert
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.Count > 0)), Times.Once);
    }

    [Fact]
    public async Task IngestFileStreamAsync_HybridMode_ProducesChunks()
    {
        // Arrange
        var document = CreateDocument("Hybrid mode content for chunking.", "test.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "test.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        // LLM returns valid JSON for hybrid splitter refinement
        _mockLlm.Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse { Content = "[\"Hybrid mode content for chunking.\"]" });

        using var stream = new MemoryStream("Hybrid mode content for chunking."u8.ToArray());

        // Act
        await CollectEventsAsync(_service.IngestFileStreamAsync(stream, "test.md", "hybrid"));

        // Assert
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.Count > 0)), Times.Once);
    }

    [Fact]
    public async Task IngestFileStreamAsync_UnknownMode_DefaultsToNlp()
    {
        // Arrange
        var document = CreateDocument("Unknown mode content.", "doc.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "doc.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("Unknown mode content."u8.ToArray());

        // Act -- invalid mode should default to nlp
        await CollectEventsAsync(_service.IngestFileStreamAsync(stream, "doc.md", "invalid_mode"));

        // Assert -- should still produce chunks without throwing
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.Count > 0)), Times.Once);
    }

    // --- SSE-specific tests ---

    [Fact]
    public async Task IngestFileStreamAsync_EmitsLoadingStatusEvent()
    {
        // Arrange
        var document = CreateDocument("Content", "test.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "test.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("Content"u8.ToArray());

        // Act
        var events = await CollectEventsAsync(
            _service.IngestFileStreamAsync(stream, "test.md"));

        // Assert
        events.Should().Contain(e => e.Type == "status" && e.Message.Contains("Loading"));
    }

    [Fact]
    public async Task IngestFileStreamAsync_EmitsChunkingStatusEvent()
    {
        // Arrange
        var document = CreateDocument("Content", "test.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "test.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("Content"u8.ToArray());

        // Act
        var events = await CollectEventsAsync(
            _service.IngestFileStreamAsync(stream, "test.md"));

        // Assert
        events.Should().Contain(e => e.Type == "status" && e.Message.Contains("Chunking"));
    }

    [Fact]
    public async Task IngestFileStreamAsync_EmitsUpsertingStatusEvent()
    {
        // Arrange
        var document = CreateDocument("Content", "test.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "test.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("Content"u8.ToArray());

        // Act
        var events = await CollectEventsAsync(
            _service.IngestFileStreamAsync(stream, "test.md"));

        // Assert
        events.Should().Contain(e => e.Type == "status" && e.Message.Contains("Upserting"));
    }

    [Fact]
    public async Task IngestFileStreamAsync_HybridMode_EmitsProgressEvents()
    {
        // Arrange
        var text = """
            # Introduction

            This is a paragraph with enough text for the NLP splitter to produce segments.
            It discusses retrieval-augmented generation and how it improves answer quality.
            The approach combines vector search with large language model capabilities.

            # Architecture

            The system architecture consists of multiple components working together.
            The ingestion pipeline processes documents through chunking and embedding stages.
            """;
        var document = CreateDocument(text, "test.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "test.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        _mockLlm.Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse { Content = "[\"chunk one\", \"chunk two\"]" });

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));

        // Act
        var events = await CollectEventsAsync(
            _service.IngestFileStreamAsync(stream, "test.md", "hybrid"));

        // Assert
        events.Should().Contain(e => e.Type == "status" && e.Message.Contains("NLP pre-chunking"));
        events.Should().Contain(e => e.Type == "status" && e.Message.Contains("LLM refining"));
    }

    [Fact]
    public async Task IngestUrlStreamAsync_LoaderThrows_EmitsErrorEvent()
    {
        // Arrange
        _urlLoader.Setup(l => l.LoadAsync("https://bad.example.com"))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        // Act
        var events = await CollectEventsAsync(
            _service.IngestUrlStreamAsync("https://bad.example.com"));

        // Assert
        var lastEvent = events.Last();
        lastEvent.Type.Should().Be("error");
        lastEvent.Message.Should().Contain("Connection refused");
    }

    // --- B18: Content hash and replace tests ---

    [Fact]
    public async Task IngestFileStreamAsync_SetsContentHashOnChunks()
    {
        // Arrange
        var document = CreateDocument("Content for hashing", "test.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "test.md"))
            .ReturnsAsync(document);

        List<DocumentChunk>? capturedChunks = null;
        _pineconeService.Setup(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()))
            .Callback<List<DocumentChunk>>(chunks => capturedChunks = chunks)
            .Returns(Task.CompletedTask);

        using var stream = new MemoryStream("Content for hashing"u8.ToArray());

        // Act
        await CollectEventsAsync(_service.IngestFileStreamAsync(stream, "test.md"));

        // Assert
        capturedChunks.Should().NotBeNull();
        capturedChunks!.Should().AllSatisfy(c =>
        {
            c.ContentHash.Should().NotBeNullOrEmpty();
            // SHA-256 hex is 64 characters
            c.ContentHash.Should().HaveLength(64);
            // Should be lowercase hex
            c.ContentHash.Should().MatchRegex("^[0-9a-f]{64}$");
        });

        // All chunks from the same document should have the same hash
        capturedChunks.Select(c => c.ContentHash).Distinct().Should().HaveCount(1);
    }

    [Fact]
    public async Task IngestFileStreamAsync_WithReplace_EmitsReplacingEvent()
    {
        // Arrange
        var document = CreateDocument("New content", "test.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "test.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();
        _pineconeService.Setup(p => p.DeleteBySourceAsync("test.md"))
            .Returns(Task.CompletedTask);

        using var stream = new MemoryStream("New content"u8.ToArray());

        // Act
        var events = await CollectEventsAsync(
            _service.IngestFileStreamAsync(stream, "test.md", "nlp", replace: true));

        // Assert
        events.Should().Contain(e => e.Type == "status" && e.Message.Contains("Replacing"));
        events.Should().Contain(e => e.Type == "done");
        _pineconeService.Verify(p => p.DeleteBySourceAsync("test.md"), Times.Once);
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()), Times.Once);
    }

    [Fact]
    public async Task IngestFileStreamAsync_WithReplaceFalse_DoesNotDelete()
    {
        // Arrange
        var document = CreateDocument("Content", "test.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "test.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("Content"u8.ToArray());

        // Act
        await CollectEventsAsync(
            _service.IngestFileStreamAsync(stream, "test.md", "nlp", replace: false));

        // Assert
        _pineconeService.Verify(p => p.DeleteBySourceAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task IngestFileStreamAsync_WithContentHash_UsesProvidedHash()
    {
        // Arrange
        var document = CreateDocument("Content", "test.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "test.md"))
            .ReturnsAsync(document);

        List<DocumentChunk>? capturedChunks = null;
        _pineconeService.Setup(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()))
            .Callback<List<DocumentChunk>>(chunks => capturedChunks = chunks)
            .Returns(Task.CompletedTask);

        using var stream = new MemoryStream("Content"u8.ToArray());

        // Act
        await CollectEventsAsync(
            _service.IngestFileStreamAsync(stream, "test.md", "nlp", false, "precomputed_hash_value"));

        // Assert
        capturedChunks.Should().NotBeNull();
        capturedChunks!.Should().AllSatisfy(c =>
            c.ContentHash.Should().Be("precomputed_hash_value"));
    }

    [Fact]
    public async Task IngestUrlStreamAsync_WithReplace_DeletesOldAndIngests()
    {
        // Arrange
        var document = CreateDocument("New URL content", "https://example.com");
        _urlLoader.Setup(l => l.LoadAsync("https://example.com"))
            .ReturnsAsync(document);
        SetupPineconeStore();
        _pineconeService.Setup(p => p.DeleteBySourceAsync("https://example.com"))
            .Returns(Task.CompletedTask);

        // Act
        var events = await CollectEventsAsync(
            _service.IngestUrlStreamAsync("https://example.com", "nlp", replace: true));

        // Assert
        events.Should().Contain(e => e.Type == "status" && e.Message.Contains("Replacing"));
        events.Should().Contain(e => e.Type == "done");
        _pineconeService.Verify(p => p.DeleteBySourceAsync("https://example.com"), Times.Once);
    }

    // --- B20: Project tagging tests ---

    [Fact]
    public async Task IngestFileStreamAsync_WithProject_SetsNormalizedProjectOnAllChunks()
    {
        // Arrange
        var document = CreateDocument("Content for project tagging", "test.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "test.md"))
            .ReturnsAsync(document);

        List<DocumentChunk>? capturedChunks = null;
        _pineconeService.Setup(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()))
            .Callback<List<DocumentChunk>>(chunks => capturedChunks = chunks)
            .Returns(Task.CompletedTask);

        using var stream = new MemoryStream("Content for project tagging"u8.ToArray());

        // Act
        await CollectEventsAsync(
            _service.IngestFileStreamAsync(stream, "test.md", "nlp", false, null, project: "my project"));

        // Assert
        capturedChunks.Should().NotBeNull();
        capturedChunks!.Should().AllSatisfy(c =>
            c.Project.Should().Be("MY-PROJECT"));
    }

    [Fact]
    public async Task IngestFileStreamAsync_WithoutProject_ChunksHaveEmptyProject()
    {
        // Arrange
        var document = CreateDocument("Content", "test.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "test.md"))
            .ReturnsAsync(document);

        List<DocumentChunk>? capturedChunks = null;
        _pineconeService.Setup(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()))
            .Callback<List<DocumentChunk>>(chunks => capturedChunks = chunks)
            .Returns(Task.CompletedTask);

        using var stream = new MemoryStream("Content"u8.ToArray());

        // Act
        await CollectEventsAsync(_service.IngestFileStreamAsync(stream, "test.md"));

        // Assert
        capturedChunks.Should().NotBeNull();
        capturedChunks!.Should().AllSatisfy(c =>
            c.Project.Should().Be(""));
    }

    [Fact]
    public async Task IngestFileStreamAsync_WithProject_EmitsProjectTaggingEvent()
    {
        // Arrange
        var document = CreateDocument("Content", "test.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "test.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("Content"u8.ToArray());

        // Act
        var events = await CollectEventsAsync(
            _service.IngestFileStreamAsync(stream, "test.md", "nlp", false, null, project: "nesa"));

        // Assert
        events.Should().Contain(e => e.Type == "status" && e.Message.Contains("Tagging with project: NESA"));
    }

    [Fact]
    public async Task IngestUrlStreamAsync_WithProject_SetsNormalizedProjectOnAllChunks()
    {
        // Arrange
        var document = CreateDocument("URL content", "https://example.com");
        _urlLoader.Setup(l => l.LoadAsync("https://example.com"))
            .ReturnsAsync(document);

        List<DocumentChunk>? capturedChunks = null;
        _pineconeService.Setup(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()))
            .Callback<List<DocumentChunk>>(chunks => capturedChunks = chunks)
            .Returns(Task.CompletedTask);

        // Act
        await CollectEventsAsync(
            _service.IngestUrlStreamAsync("https://example.com", "nlp", false, null, project: "Project - A"));

        // Assert
        capturedChunks.Should().NotBeNull();
        capturedChunks!.Should().AllSatisfy(c =>
            c.Project.Should().Be("PROJECT-A"));
    }

    [Fact]
    public void ComputeSha256Hash_ReturnsLowercaseHex()
    {
        // Act
        var hash = IngestionService.ComputeSha256Hash("test content");

        // Assert
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ComputeSha256Hash_SameInputProducesSameHash()
    {
        // Act
        var hash1 = IngestionService.ComputeSha256Hash("identical content");
        var hash2 = IngestionService.ComputeSha256Hash("identical content");

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeSha256Hash_DifferentInputProducesDifferentHash()
    {
        // Act
        var hash1 = IngestionService.ComputeSha256Hash("content A");
        var hash2 = IngestionService.ComputeSha256Hash("content B");

        // Assert
        hash1.Should().NotBe(hash2);
    }

    // --- B23: PDF and Word file type support ---

    /// <summary>
    /// Builds a minimal text-based PDF byte array using PdfPig's PdfDocumentBuilder.
    /// </summary>
    private static byte[] BuildTextPdf(string text)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var page = builder.AddPage(595, 842);
        page.AddText(text, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 800), font);
        return builder.Build();
    }

    /// <summary>
    /// Builds a minimal in-memory DOCX with a single paragraph.
    /// </summary>
    private static byte[] BuildSimpleDocx(string text)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new OpenXmlDocument();
            var body = mainPart.Document.AppendChild(new Body());
            var p = new Paragraph();
            var run = new Run();
            run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            p.AppendChild(run);
            body.AppendChild(p);
            mainPart.Document.Save();
        }
        return stream.ToArray();
    }

    [Fact]
    public async Task IngestFileStreamAsync_PdfFile_ConvertsAndIngests()
    {
        // Arrange — a real text-based PDF stream. The TextFileLoader mock should NOT be called;
        // PDFs go through DocumentConverter directly inside IngestionService.
        SetupPineconeStore();
        var pdfBytes = BuildTextPdf("Hello from inside the PDF document.");
        using var stream = new MemoryStream(pdfBytes);

        List<DocumentChunk>? capturedChunks = null;
        _pineconeService.Setup(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()))
            .Callback<List<DocumentChunk>>(chunks => capturedChunks = chunks)
            .Returns(Task.CompletedTask);

        // Act
        var events = await CollectEventsAsync(
            _service.IngestFileStreamAsync(stream, "report.pdf"));

        // Assert
        events.Should().Contain(e => e.Type == "status" && e.Message.Contains("Converting PDF"));
        var doneEvent = events.Last();
        doneEvent.Type.Should().Be("done");
        doneEvent.Message.Should().Contain("report.pdf");

        capturedChunks.Should().NotBeNull();
        capturedChunks!.Should().NotBeEmpty();
        capturedChunks.Should().AllSatisfy(c => c.Source.Should().Be("report.pdf"));
        // The extracted PDF text should be present in at least one chunk.
        capturedChunks.Should().Contain(c => c.Content.Contains("Hello from inside the PDF"));

        // The TextFileLoader should NOT be involved for PDFs.
        _documentLoader.Verify(l => l.LoadAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task IngestFileStreamAsync_DocxFile_ConvertsAndIngests()
    {
        // Arrange
        SetupPineconeStore();
        var docxBytes = BuildSimpleDocx("Hello from a Word document body.");
        using var stream = new MemoryStream(docxBytes);

        List<DocumentChunk>? capturedChunks = null;
        _pineconeService.Setup(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()))
            .Callback<List<DocumentChunk>>(chunks => capturedChunks = chunks)
            .Returns(Task.CompletedTask);

        // Act
        var events = await CollectEventsAsync(
            _service.IngestFileStreamAsync(stream, "memo.docx"));

        // Assert
        events.Should().Contain(e => e.Type == "status" && e.Message.Contains("Converting Word"));
        var doneEvent = events.Last();
        doneEvent.Type.Should().Be("done");
        doneEvent.Message.Should().Contain("memo.docx");

        capturedChunks.Should().NotBeNull();
        capturedChunks!.Should().NotBeEmpty();
        capturedChunks.Should().AllSatisfy(c => c.Source.Should().Be("memo.docx"));
        capturedChunks.Should().Contain(c => c.Content.Contains("Hello from a Word document"));

        _documentLoader.Verify(l => l.LoadAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task IngestFileStreamAsync_UnsupportedExtension_YieldsErrorEvent()
    {
        // Arrange — .exe is not supported.
        using var stream = new MemoryStream("anything"u8.ToArray());

        // Act
        var events = await CollectEventsAsync(
            _service.IngestFileStreamAsync(stream, "virus.exe"));

        // Assert
        var lastEvent = events.Last();
        lastEvent.Type.Should().Be("error");
        lastEvent.Message.Should().Contain("Unsupported file type");
        lastEvent.Message.Should().Contain(".exe");

        // Pipeline must not proceed to chunk/store.
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()), Times.Never);
        _documentLoader.Verify(l => l.LoadAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task IngestFileStreamAsync_PdfConversionFailure_YieldsErrorEventAndDoesNotIngest()
    {
        // Arrange — random bytes that aren't a valid PDF.
        var garbage = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0x42, 0x43 };
        using var stream = new MemoryStream(garbage);

        // Act
        var events = await CollectEventsAsync(
            _service.IngestFileStreamAsync(stream, "broken.pdf"));

        // Assert
        var lastEvent = events.Last();
        lastEvent.Type.Should().Be("error");
        lastEvent.Message.Should().Contain("PDF conversion failed");

        // No chunks should be ingested when conversion fails.
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()), Times.Never);
    }

    [Fact]
    public async Task IngestFileStreamAsync_ImageOnlyPdf_YieldsFriendlyError()
    {
        // Arrange — build a PDF with a page but no text content.
        var builder = new PdfDocumentBuilder();
        builder.AddPage(595, 842);
        var pdfBytes = builder.Build();
        using var stream = new MemoryStream(pdfBytes);

        // Act
        var events = await CollectEventsAsync(
            _service.IngestFileStreamAsync(stream, "scanned.pdf"));

        // Assert
        var lastEvent = events.Last();
        lastEvent.Type.Should().Be("error");
        lastEvent.Message.Should().Contain("PDF conversion failed");
        lastEvent.Message.Should().Contain("scanned");
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()), Times.Never);
    }
}
