using RagChatbot.Core.Models;

namespace RagChatbot.Core.Interfaces;

/// <summary>
/// Orchestrates document ingestion: loading, splitting, and storing in the vector store.
/// Returns SSE events for real-time progress streaming.
/// </summary>
public interface IIngestionService
{
    /// <summary>
    /// Ingests an uploaded file with SSE streaming progress.
    /// Yields status events at each pipeline stage and a final done/error event.
    /// </summary>
    /// <param name="fileStream">The uploaded file stream.</param>
    /// <param name="originalFileName">Original filename to use as source metadata.</param>
    /// <param name="chunkingMode">Chunking mode: "fixed", "nlp", "smart", or "hybrid". Defaults to "nlp".</param>
    /// <param name="replace">If true, delete existing chunks for this source before ingesting.</param>
    /// <param name="contentHash">Pre-computed SHA-256 hash of the content. If null, will be computed.</param>
    /// <param name="project">Optional project name to tag chunks with.</param>
    /// <returns>Async enumerable of SSE events.</returns>
    IAsyncEnumerable<IngestSseEvent> IngestFileStreamAsync(
        Stream fileStream,
        string originalFileName,
        string chunkingMode = "nlp",
        bool replace = false,
        string? contentHash = null,
        string? project = null);

    /// <summary>
    /// Ingests content from a URL with SSE streaming progress.
    /// Yields status events at each pipeline stage and a final done/error event.
    /// </summary>
    /// <param name="url">The URL to fetch and ingest.</param>
    /// <param name="chunkingMode">Chunking mode: "fixed", "nlp", "smart", or "hybrid". Defaults to "nlp".</param>
    /// <param name="replace">If true, delete existing chunks for this source before ingesting.</param>
    /// <param name="contentHash">Pre-computed SHA-256 hash of the content. If null, will be computed.</param>
    /// <param name="project">Optional project name to tag chunks with.</param>
    /// <returns>Async enumerable of SSE events.</returns>
    IAsyncEnumerable<IngestSseEvent> IngestUrlStreamAsync(
        string url,
        string chunkingMode = "nlp",
        bool replace = false,
        string? contentHash = null,
        string? project = null);

    /// <summary>
    /// Ingests raw text content with SSE streaming progress.
    /// Creates a Document directly from the text (no file I/O), then follows
    /// the standard chunking and upsert pipeline.
    /// </summary>
    /// <param name="content">The raw text content to ingest.</param>
    /// <param name="source">Source name identifier for the document.</param>
    /// <param name="chunkingMode">Chunking mode: "fixed", "nlp", "smart", or "hybrid". Defaults to "nlp".</param>
    /// <param name="replace">If true, delete existing chunks for this source before ingesting.</param>
    /// <param name="contentHash">Pre-computed SHA-256 hash of the content. If null, will be computed.</param>
    /// <param name="project">Optional project name to tag chunks with.</param>
    /// <returns>Async enumerable of SSE events.</returns>
    IAsyncEnumerable<IngestSseEvent> IngestTextStreamAsync(
        string content,
        string source,
        string chunkingMode = "nlp",
        bool replace = false,
        string? contentHash = null,
        string? project = null);
}
