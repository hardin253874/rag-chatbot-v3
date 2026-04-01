using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.DocumentProcessing;

namespace RagChatbot.Infrastructure.Ingestion;

/// <summary>
/// Orchestrates document ingestion: loading, splitting, and storing in Pinecone.
/// Supports multiple chunking modes: fixed, nlp, and smart.
/// </summary>
public class IngestionService : IIngestionService
{
    private readonly IDocumentLoader _documentLoader;
    private readonly IUrlLoader _urlLoader;
    private readonly IPineconeService _pineconeService;
    private readonly RecursiveCharacterSplitter _fixedSplitter;
    private readonly NlpChunkingSplitter _nlpSplitter;
    private readonly SmartChunkingSplitter _smartSplitter;

    public IngestionService(
        IDocumentLoader documentLoader,
        IUrlLoader urlLoader,
        IPineconeService pineconeService,
        RecursiveCharacterSplitter fixedSplitter,
        NlpChunkingSplitter nlpSplitter,
        SmartChunkingSplitter smartSplitter)
    {
        _documentLoader = documentLoader;
        _urlLoader = urlLoader;
        _pineconeService = pineconeService;
        _fixedSplitter = fixedSplitter;
        _nlpSplitter = nlpSplitter;
        _smartSplitter = smartSplitter;
    }

    /// <inheritdoc />
    public async Task<string> IngestFileAsync(Stream fileStream, string originalFileName, string chunkingMode = "nlp")
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

            // Split using the selected splitter
            var splitter = SelectSplitter(chunkingMode);
            var chunks = splitter.Split(document);

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
    public async Task<string> IngestUrlAsync(string url, string chunkingMode = "nlp")
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL must not be empty.", nameof(url));

        // Load document from URL
        var document = await _urlLoader.LoadAsync(url);

        // Split using the selected splitter
        var splitter = SelectSplitter(chunkingMode);
        var chunks = splitter.Split(document);

        // Store in Pinecone
        await _pineconeService.StoreDocumentsAsync(chunks);

        return $"Ingested URL: {url}";
    }

    private ITextSplitter SelectSplitter(string chunkingMode)
    {
        return chunkingMode switch
        {
            "fixed" => _fixedSplitter,
            "nlp" => _nlpSplitter,
            "smart" => _smartSplitter,
            _ => _nlpSplitter
        };
    }
}
