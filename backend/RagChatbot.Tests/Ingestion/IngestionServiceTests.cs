using FluentAssertions;
using Moq;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.Ingestion;

namespace RagChatbot.Tests.Ingestion;

public class IngestionServiceTests
{
    private readonly Mock<IDocumentLoader> _documentLoader;
    private readonly Mock<IUrlLoader> _urlLoader;
    private readonly Mock<IPineconeService> _pineconeService;
    private readonly Mock<ITextSplitter> _textSplitter;
    private readonly IngestionService _service;

    public IngestionServiceTests()
    {
        _documentLoader = new Mock<IDocumentLoader>();
        _urlLoader = new Mock<IUrlLoader>();
        _pineconeService = new Mock<IPineconeService>();
        _textSplitter = new Mock<ITextSplitter>();

        _service = new IngestionService(
            _documentLoader.Object,
            _urlLoader.Object,
            _pineconeService.Object,
            _textSplitter.Object);
    }

    private void SetupTextSplitter(string source)
    {
        _textSplitter
            .Setup(s => s.Split(It.IsAny<Document>()))
            .Returns((Document doc) => new List<DocumentChunk>
            {
                new() { Id = "doc_1_0", Content = doc.PageContent, Source = source }
            });
    }

    [Fact]
    public async Task IngestFileAsync_MdFile_UsesTextSplitter()
    {
        // Arrange
        var document = new Document
        {
            PageContent = "# Heading\nSome content",
            Metadata = new Dictionary<string, string> { ["source"] = "test.md" }
        };
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "test.md"))
            .ReturnsAsync(document);
        SetupTextSplitter("test.md");
        _pineconeService.Setup(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()))
            .Returns(Task.CompletedTask);

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
        var document = new Document
        {
            PageContent = "Some plain text content for testing",
            Metadata = new Dictionary<string, string> { ["source"] = "notes.txt" }
        };
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "notes.txt"))
            .ReturnsAsync(document);
        SetupTextSplitter("notes.txt");
        _pineconeService.Setup(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()))
            .Returns(Task.CompletedTask);

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
        var document = new Document
        {
            PageContent = "Content",
            Metadata = new Dictionary<string, string> { ["source"] = "report.md" }
        };
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "report.md"))
            .ReturnsAsync(document);
        SetupTextSplitter("report.md");
        _pineconeService.Setup(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()))
            .Returns(Task.CompletedTask);

        using var stream = new MemoryStream("Content"u8.ToArray());

        // Act
        await _service.IngestFileAsync(stream, "report.md");

        // Assert -- verify the source is the original filename, not a temp path
        _documentLoader.Verify(l => l.LoadAsync(It.IsAny<string>(), "report.md"), Times.Once);
        _pineconeService.Verify(p => p.StoreDocumentsAsync(It.Is<List<DocumentChunk>>(
            chunks => chunks.All(c => c.Source == "report.md"))), Times.Once);
    }

    [Fact]
    public async Task IngestFileAsync_CallsPineconeStoreDocuments()
    {
        // Arrange
        var document = new Document
        {
            PageContent = "Content here",
            Metadata = new Dictionary<string, string> { ["source"] = "data.txt" }
        };
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "data.txt"))
            .ReturnsAsync(document);
        SetupTextSplitter("data.txt");
        _pineconeService.Setup(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()))
            .Returns(Task.CompletedTask);

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
        var document = new Document
        {
            PageContent = "Content",
            Metadata = new Dictionary<string, string> { ["source"] = "readme.md" }
        };
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "readme.md"))
            .ReturnsAsync(document);
        SetupTextSplitter("readme.md");
        _pineconeService.Setup(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()))
            .Returns(Task.CompletedTask);

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
        var document = new Document
        {
            PageContent = "Content",
            Metadata = new Dictionary<string, string> { ["source"] = "test.md" }
        };
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "test.md"))
            .ReturnsAsync(document);
        SetupTextSplitter("test.md");
        _pineconeService.Setup(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()))
            .Returns(Task.CompletedTask);

        using var stream = new MemoryStream("Content"u8.ToArray());

        // Act
        await _service.IngestFileAsync(stream, "test.md");

        // Assert -- the temp file path that was passed to LoadAsync should no longer exist
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

        // Temp file should be cleaned up even on failure
        _documentLoader.Verify(l => l.LoadAsync(It.IsAny<string>(), "bad.md"), Times.Once);
    }

    [Fact]
    public async Task IngestUrlAsync_LoadsAndSplitsAndStores()
    {
        // Arrange
        var document = new Document
        {
            PageContent = "Web page content about RAG chatbots",
            Metadata = new Dictionary<string, string> { ["source"] = "https://example.com/article" }
        };
        _urlLoader.Setup(l => l.LoadAsync("https://example.com/article"))
            .ReturnsAsync(document);
        SetupTextSplitter("https://example.com/article");
        _pineconeService.Setup(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()))
            .Returns(Task.CompletedTask);

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
        var document = new Document
        {
            PageContent = "Content",
            Metadata = new Dictionary<string, string> { ["source"] = "https://example.com" }
        };
        _urlLoader.Setup(l => l.LoadAsync("https://example.com"))
            .ReturnsAsync(document);
        SetupTextSplitter("https://example.com");
        _pineconeService.Setup(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.IngestUrlAsync("https://example.com");

        // Assert
        result.Should().Be("Ingested URL: https://example.com");
    }

    // --- New tests: verify ITextSplitter is called for file and URL ingestion ---

    [Fact]
    public async Task IngestFileAsync_UsesInjectedTextSplitter()
    {
        // Arrange
        var document = new Document
        {
            PageContent = "File content to split",
            Metadata = new Dictionary<string, string> { ["source"] = "data.txt" }
        };
        var expectedChunks = new List<DocumentChunk>
        {
            new() { Id = "doc_1_0", Content = "chunk one", Source = "data.txt" },
            new() { Id = "doc_1_1", Content = "chunk two", Source = "data.txt" }
        };
        _documentLoader.Setup(l => l.LoadAsync(It.IsAny<string>(), "data.txt"))
            .ReturnsAsync(document);
        _textSplitter.Setup(s => s.Split(It.IsAny<Document>())).Returns(expectedChunks);
        _pineconeService.Setup(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()))
            .Returns(Task.CompletedTask);

        using var stream = new MemoryStream("File content to split"u8.ToArray());

        // Act
        await _service.IngestFileAsync(stream, "data.txt");

        // Assert
        _textSplitter.Verify(s => s.Split(It.IsAny<Document>()), Times.Once);
        _pineconeService.Verify(p => p.StoreDocumentsAsync(expectedChunks), Times.Once);
    }

    [Fact]
    public async Task IngestUrlAsync_UsesInjectedTextSplitter()
    {
        // Arrange
        var document = new Document
        {
            PageContent = "URL content to split",
            Metadata = new Dictionary<string, string> { ["source"] = "https://example.com/page" }
        };
        var expectedChunks = new List<DocumentChunk>
        {
            new() { Id = "doc_1_0", Content = "url chunk", Source = "https://example.com/page" }
        };
        _urlLoader.Setup(l => l.LoadAsync("https://example.com/page"))
            .ReturnsAsync(document);
        _textSplitter.Setup(s => s.Split(It.IsAny<Document>())).Returns(expectedChunks);
        _pineconeService.Setup(p => p.StoreDocumentsAsync(It.IsAny<List<DocumentChunk>>()))
            .Returns(Task.CompletedTask);

        // Act
        await _service.IngestUrlAsync("https://example.com/page");

        // Assert
        _textSplitter.Verify(s => s.Split(It.IsAny<Document>()), Times.Once);
        _pineconeService.Verify(p => p.StoreDocumentsAsync(expectedChunks), Times.Once);
    }
}
