using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Infrastructure.Ingestion;

/// <summary>
/// Orchestrates document ingestion: loading, splitting, and storing in Pinecone.
/// </summary>
public class IngestionService : IIngestionService
{
    private readonly IDocumentLoader _documentLoader;
    private readonly IUrlLoader _urlLoader;
    private readonly IPineconeService _pineconeService;
    private readonly ITextSplitter _textSplitter;

    public IngestionService(
        IDocumentLoader documentLoader,
        IUrlLoader urlLoader,
        IPineconeService pineconeService,
        ITextSplitter textSplitter)
    {
        _documentLoader = documentLoader;
        _urlLoader = urlLoader;
        _pineconeService = pineconeService;
        _textSplitter = textSplitter;
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

            // Split using the injected text splitter
            var chunks = _textSplitter.Split(document);

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

        // Split using the injected text splitter
        var chunks = _textSplitter.Split(document);

        // Store in Pinecone
        await _pineconeService.StoreDocumentsAsync(chunks);

        return $"Ingested URL: {url}";
    }
}
