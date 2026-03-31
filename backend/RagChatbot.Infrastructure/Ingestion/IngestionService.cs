using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.DocumentProcessing;

namespace RagChatbot.Infrastructure.Ingestion;

/// <summary>
/// Orchestrates document ingestion: loading, splitting, and storing in Pinecone.
/// </summary>
public class IngestionService : IIngestionService
{
    private readonly IDocumentLoader _documentLoader;
    private readonly IUrlLoader _urlLoader;
    private readonly IPineconeService _pineconeService;
    private readonly MarkdownSplitter _markdownSplitter;
    private readonly RecursiveCharacterSplitter _recursiveSplitter;

    public IngestionService(
        IDocumentLoader documentLoader,
        IUrlLoader urlLoader,
        IPineconeService pineconeService,
        MarkdownSplitter markdownSplitter,
        RecursiveCharacterSplitter recursiveSplitter)
    {
        _documentLoader = documentLoader;
        _urlLoader = urlLoader;
        _pineconeService = pineconeService;
        _markdownSplitter = markdownSplitter;
        _recursiveSplitter = recursiveSplitter;
    }

    /// <inheritdoc />
    public async Task<string> IngestFileAsync(Stream fileStream, string originalFileName)
    {
        if (fileStream == null)
            throw new ArgumentNullException(nameof(fileStream));
        if (string.IsNullOrWhiteSpace(originalFileName))
            throw new ArgumentException("Original file name must not be empty.", nameof(originalFileName));

        var tempPath = Path.GetTempFileName();
        try
        {
            // Save uploaded file to temp path
            using (var fileStreamOut = new FileStream(tempPath, FileMode.Create))
            {
                await fileStream.CopyToAsync(fileStreamOut);
            }

            // Load document using TextFileLoader with original filename as source
            var document = await _documentLoader.LoadAsync(tempPath, originalFileName);

            // Split using appropriate splitter based on file extension
            var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
            var chunks = extension == ".md"
                ? _markdownSplitter.Split(document)
                : _recursiveSplitter.Split(document);

            // Store in Pinecone
            await _pineconeService.StoreDocumentsAsync(chunks);

            return $"Ingested file: {originalFileName}";
        }
        finally
        {
            // Always clean up temp file
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    /// <inheritdoc />
    public async Task<string> IngestUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL must not be empty.", nameof(url));

        // Load document from URL
        var document = await _urlLoader.LoadAsync(url);

        // Split using recursive character splitter (URLs are never markdown)
        var chunks = _recursiveSplitter.Split(document);

        // Store in Pinecone
        await _pineconeService.StoreDocumentsAsync(chunks);

        return $"Ingested URL: {url}";
    }
}
