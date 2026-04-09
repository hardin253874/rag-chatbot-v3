using System.Security.Cryptography;
using System.Text;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.DocumentProcessing;

namespace RagChatbot.Infrastructure.Ingestion;

/// <summary>
/// Orchestrates document ingestion: loading, splitting, and storing in Pinecone.
/// Returns SSE events for real-time progress streaming.
/// Supports multiple chunking modes: fixed, nlp, smart, and hybrid.
/// Supports content-hash-based deduplication and document replacement.
/// </summary>
public class IngestionService : IIngestionService
{
    private readonly IDocumentLoader _documentLoader;
    private readonly IUrlLoader _urlLoader;
    private readonly IPineconeService _pineconeService;
    private readonly RecursiveCharacterSplitter _fixedSplitter;
    private readonly NlpChunkingSplitter _nlpSplitter;
    private readonly SmartChunkingSplitter _smartSplitter;
    private readonly HybridChunkingSplitter _hybridSplitter;

    public IngestionService(
        IDocumentLoader documentLoader,
        IUrlLoader urlLoader,
        IPineconeService pineconeService,
        RecursiveCharacterSplitter fixedSplitter,
        NlpChunkingSplitter nlpSplitter,
        SmartChunkingSplitter smartSplitter,
        HybridChunkingSplitter hybridSplitter)
    {
        _documentLoader = documentLoader;
        _urlLoader = urlLoader;
        _pineconeService = pineconeService;
        _fixedSplitter = fixedSplitter;
        _nlpSplitter = nlpSplitter;
        _smartSplitter = smartSplitter;
        _hybridSplitter = hybridSplitter;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IngestSseEvent> IngestFileStreamAsync(
        Stream fileStream,
        string originalFileName,
        string chunkingMode = "nlp",
        bool replace = false,
        string? contentHash = null)
    {
        if (fileStream == null)
            throw new ArgumentNullException(nameof(fileStream));
        if (string.IsNullOrWhiteSpace(originalFileName))
            throw new ArgumentException("Original file name must not be empty.", nameof(originalFileName));

        yield return new IngestSseEvent { Type = "status", Message = "Loading document..." };

        Document? document = null;
        string? loadError = null;
        var tempPath = Path.GetTempFileName();

        try
        {
            // Save uploaded file to temp path
            using (var fileStreamOut = new FileStream(tempPath, FileMode.Create))
            {
                await fileStream.CopyToAsync(fileStreamOut);
            }

            // Load document using TextFileLoader with original filename as source
            document = await _documentLoader.LoadAsync(tempPath, originalFileName);
        }
        catch (Exception ex)
        {
            loadError = $"Failed to load document: {ex.Message}";
        }
        finally
        {
            // Always clean up temp file
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        if (loadError != null)
        {
            yield return new IngestSseEvent { Type = "error", Message = loadError };
            yield break;
        }

        // Compute hash if not provided
        var hash = contentHash ?? ComputeSha256Hash(document!.PageContent);

        // Handle replacement
        if (replace)
        {
            yield return new IngestSseEvent { Type = "status", Message = $"Replacing previous version of {originalFileName}..." };

            string? deleteError = null;
            try
            {
                await _pineconeService.DeleteBySourceAsync(originalFileName);
            }
            catch (Exception ex)
            {
                deleteError = $"Failed to delete old chunks: {ex.Message}";
            }

            if (deleteError != null)
            {
                yield return new IngestSseEvent { Type = "error", Message = deleteError };
                yield break;
            }
        }

        // Chunking and storing
        await foreach (var evt in ChunkAndStoreAsync(document!, originalFileName, chunkingMode, hash))
        {
            yield return evt;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IngestSseEvent> IngestUrlStreamAsync(
        string url,
        string chunkingMode = "nlp",
        bool replace = false,
        string? contentHash = null)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL must not be empty.", nameof(url));

        yield return new IngestSseEvent { Type = "status", Message = "Loading document..." };

        Document? document = null;
        string? loadError = null;

        try
        {
            document = await _urlLoader.LoadAsync(url);
        }
        catch (Exception ex)
        {
            loadError = $"Failed to load URL: {ex.Message}";
        }

        if (loadError != null)
        {
            yield return new IngestSseEvent { Type = "error", Message = loadError };
            yield break;
        }

        // Compute hash if not provided
        var hash = contentHash ?? ComputeSha256Hash(document!.PageContent);

        // Handle replacement
        if (replace)
        {
            yield return new IngestSseEvent { Type = "status", Message = $"Replacing previous version of {url}..." };

            string? deleteError = null;
            try
            {
                await _pineconeService.DeleteBySourceAsync(url);
            }
            catch (Exception ex)
            {
                deleteError = $"Failed to delete old chunks: {ex.Message}";
            }

            if (deleteError != null)
            {
                yield return new IngestSseEvent { Type = "error", Message = deleteError };
                yield break;
            }
        }

        // Chunking and storing
        await foreach (var evt in ChunkAndStoreAsync(document!, url, chunkingMode, hash))
        {
            yield return evt;
        }
    }

    private async IAsyncEnumerable<IngestSseEvent> ChunkAndStoreAsync(
        Document document,
        string sourceName,
        string chunkingMode,
        string contentHash)
    {
        yield return new IngestSseEvent { Type = "status", Message = $"Chunking with mode: {chunkingMode}..." };

        List<DocumentChunk>? chunks = null;
        string? chunkError = null;
        var progressMessages = new List<string>();

        try
        {
            var splitter = SelectSplitter(chunkingMode);

            // Wire up hybrid progress callback
            if (splitter is HybridChunkingSplitter hybrid)
            {
                hybrid.WithProgress(msg => progressMessages.Add(msg));
            }

            chunks = splitter.Split(document);

            // Set content hash on all chunks
            foreach (var chunk in chunks)
            {
                chunk.ContentHash = contentHash;
            }
        }
        catch (Exception ex)
        {
            chunkError = $"Chunking failed: {ex.Message}";
        }

        // Yield progress messages from hybrid mode
        foreach (var msg in progressMessages)
        {
            yield return new IngestSseEvent { Type = "status", Message = msg };
        }

        if (chunkError != null)
        {
            yield return new IngestSseEvent { Type = "error", Message = chunkError };
            yield break;
        }

        yield return new IngestSseEvent { Type = "status", Message = $"Upserting {chunks!.Count} chunks to vector store..." };

        string? upsertError = null;

        try
        {
            await _pineconeService.StoreDocumentsAsync(chunks);
        }
        catch (Exception ex)
        {
            upsertError = $"Upsert failed: {ex.Message}";
        }

        if (upsertError != null)
        {
            yield return new IngestSseEvent { Type = "error", Message = upsertError };
            yield break;
        }

        yield return new IngestSseEvent
        {
            Type = "done",
            Message = $"Ingested {sourceName} ({chunks.Count} chunks)",
            Chunks = chunks.Count
        };
    }

    /// <summary>
    /// Computes a lowercase hex SHA-256 hash of the given text.
    /// </summary>
    internal static string ComputeSha256Hash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private ITextSplitter SelectSplitter(string chunkingMode)
    {
        return chunkingMode switch
        {
            "fixed" => _fixedSplitter,
            "nlp" => _nlpSplitter,
            "smart" => _smartSplitter,
            "hybrid" => _hybridSplitter,
            _ => _nlpSplitter
        };
    }
}
