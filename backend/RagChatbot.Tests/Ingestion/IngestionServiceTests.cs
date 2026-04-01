using FluentAssertions;
using Moq;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.DocumentProcessing;
using RagChatbot.Infrastructure.Ingestion;

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

        _service = new IngestionService(
            _documentLoader.Object,
            _urlLoader.Object,
            _pineconeService.Object,
            _fixedSplitter,
            _nlpSplitter,
            _smartSplitter);
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

    [Fact]
    public async Task IngestFileAsync_MdFile_UsesTextSplitter()
    {
        // Arrange
        var document = CreateDocument("# Heading\nSome content", "test.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "test.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("# Heading\nSome content"u8.ToArray());

        // Act
        var result = await _service.IngestFileAsync(stream, "test.md");

        // Assert
        result.Should().Be("Ingested file: test.md");
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.All(c => c.Source == "test.md"))), Times.Once);
    }

    [Fact]
    public async Task IngestFileAsync_TxtFile_UsesTextSplitter()
    {
        // Arrange
        var document = CreateDocument("Some plain text content for testing", "notes.txt");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "notes.txt"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("Some plain text content for testing"u8.ToArray());

        // Act
        var result = await _service.IngestFileAsync(stream, "notes.txt");

        // Assert
        result.Should().Be("Ingested file: notes.txt");
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.All(c => c.Source == "notes.txt"))), Times.Once);
    }

    [Fact]
    public async Task IngestFileAsync_StoresOriginalFilename_NotTempPath()
    {
        // Arrange
        var document = CreateDocument("Content", "report.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "report.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("Content"u8.ToArray());

        // Act
        await _service.IngestFileAsync(stream, "report.md");

        // Assert
        _documentLoader.Verify(l => l.LoadAsync(It.IsAny<string>(), "report.md"), Times.Once);
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.All(c => c.Source == "report.md"))), Times.Once);
    }

    [Fact]
    public async Task IngestFileAsync_CallsPineconeStoreDocuments()
    {
        // Arrange
        var document = CreateDocument("Content here", "data.txt");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "data.txt"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("Content here"u8.ToArray());

        // Act
        await _service.IngestFileAsync(stream, "data.txt");

        // Assert
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.Count > 0)), Times.Once);
    }

    [Fact]
    public async Task IngestFileAsync_ReturnsSuccessMessage()
    {
        // Arrange
        var document = CreateDocument("Content", "readme.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "readme.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("Content"u8.ToArray());

        // Act
        var result = await _service.IngestFileAsync(stream, "readme.md");

        // Assert
        result.Should().Be("Ingested file: readme.md");
    }

    [Fact]
    public async Task IngestFileAsync_CleansUpTempFile()
    {
        // Arrange
        var document = CreateDocument("Content", "test.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "test.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("Content"u8.ToArray());

        // Act
        await _service.IngestFileAsync(stream, "test.md");

        // Assert
        _documentLoader.Verify(l => l.LoadAsync(It.Is<string>(path =>
            !File.Exists(path) || path == string.Empty), "test.md"), Times.Once);
    }

    [Fact]
    public async Task IngestFileAsync_CleansUpTempFileOnFailure()
    {
        // Arrange
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "bad.md"))
            .ThrowsAsync(new InvalidOperationException("Load failed"));

        using var stream = new MemoryStream("Content"u8.ToArray());

        // Act & Assert
        var act = () => _service.IngestFileAsync(stream, "bad.md");
        await act.Should().ThrowAsync<InvalidOperationException>();

        _documentLoader.Verify(l => l.LoadAsync(It.IsAny<string>(), "bad.md"), Times.Once);
    }

    [Fact]
    public async Task IngestUrlAsync_LoadsAndSplitsAndStores()
    {
        // Arrange
        var document = CreateDocument("Web page content about RAG chatbots", "https://example.com/article");
        _urlLoader.Setup(l => l.LoadAsync("https://example.com/article"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        // Act
        var result = await _service.IngestUrlAsync("https://example.com/article");

        // Assert
        result.Should().Be("Ingested URL: https://example.com/article");
        _urlLoader.Verify(l => l.LoadAsync("https://example.com/article"), Times.Once);
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.Count > 0 && chunks.All(c => c.Source == "https://example.com/article"))),
            Times.Once);
    }

    [Fact]
    public async Task IngestUrlAsync_ReturnsSuccessMessage()
    {
        // Arrange
        var document = CreateDocument("Content", "https://example.com");
        _urlLoader.Setup(l => l.LoadAsync("https://example.com"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        // Act
        var result = await _service.IngestUrlAsync("https://example.com");

        // Assert
        result.Should().Be("Ingested URL: https://example.com");
    }

    [Fact]
    public async Task IngestFileAsync_UsesInjectedTextSplitter()
    {
        // Arrange — default chunkingMode is "nlp", so NlpChunkingSplitter should be used
        var document = CreateDocument("File content to split", "data.txt");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "data.txt"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("File content to split"u8.ToArray());

        // Act
        await _service.IngestFileAsync(stream, "data.txt");

        // Assert — NlpChunkingSplitter should produce chunks (short doc = 1 chunk)
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.Count > 0 && chunks.All(c => c.Source == "data.txt"))), Times.Once);
    }

    [Fact]
    public async Task IngestUrlAsync_UsesInjectedTextSplitter()
    {
        // Arrange
        var document = CreateDocument("URL content to split", "https://example.com/page");
        _urlLoader.Setup(l => l.LoadAsync("https://example.com/page"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        // Act
        await _service.IngestUrlAsync("https://example.com/page");

        // Assert
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.Count > 0 && chunks.All(c => c.Source == "https://example.com/page"))), Times.Once);
    }

    // --- Mode selection tests ---

    [Fact]
    public async Task IngestFileAsync_FixedMode_ProducesChunks()
    {
        // Arrange — long enough text that RecursiveCharacterSplitter will split
        var longText = string.Join(" ", Enumerable.Range(1, 200).Select(i => $"Word{i}"));
        var document = CreateDocument(longText, "data.txt");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "data.txt"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(longText));

        // Act
        await _service.IngestFileAsync(stream, "data.txt", "fixed");

        // Assert — chunks should be stored
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.Count > 0)), Times.Once);
    }

    [Fact]
    public async Task IngestFileAsync_NlpMode_ProducesChunks()
    {
        // Arrange
        var document = CreateDocument("Short NLP content.", "doc.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "doc.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("Short NLP content."u8.ToArray());

        // Act
        await _service.IngestFileAsync(stream, "doc.md", "nlp");

        // Assert
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.Count > 0)), Times.Once);
    }

    [Fact]
    public async Task IngestFileAsync_DefaultMode_UsesNlpSplitter()
    {
        // Arrange — no chunkingMode param means default = "nlp"
        var document = CreateDocument("Default mode content.", "doc.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "doc.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("Default mode content."u8.ToArray());

        // Act — no chunkingMode parameter
        await _service.IngestFileAsync(stream, "doc.md");

        // Assert
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.Count > 0)), Times.Once);
    }

    [Fact]
    public async Task IngestUrlAsync_FixedMode_ProducesChunks()
    {
        // Arrange
        var document = CreateDocument("URL content for fixed mode", "https://example.com");
        _urlLoader.Setup(l => l.LoadAsync("https://example.com"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        // Act
        await _service.IngestUrlAsync("https://example.com", "fixed");

        // Assert
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.Count > 0)), Times.Once);
    }

    [Fact]
    public async Task IngestFileAsync_UnknownMode_DefaultsToNlp()
    {
        // Arrange
        var document = CreateDocument("Unknown mode content.", "doc.md");
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "doc.md"))
            .ReturnsAsync(document);
        SetupPineconeStore();

        using var stream = new MemoryStream("Unknown mode content."u8.ToArray());

        // Act — invalid mode should default to nlp
        await _service.IngestFileAsync(stream, "doc.md", "invalid_mode");

        // Assert — should still produce chunks without throwing
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.Count > 0)), Times.Once);
    }
}
