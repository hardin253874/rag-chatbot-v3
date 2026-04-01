namespace RagChatbot.Core.Interfaces;

/// <summary>
/// Orchestrates document ingestion: loading, splitting, and storing in the vector store.
/// </summary>
public interface IIngestionService
{
    /// <summary>
    /// Ingests an uploaded file: saves to temp, loads, splits, stores, cleans up.
    /// </summary>
    /// <param name="fileStream">The uploaded file stream.</param>
    /// <param name="originalFileName">Original filename to use as source metadata.</param>
    /// <param name="chunkingMode">Chunking mode: "fixed", "nlp", or "smart". Defaults to "nlp".</param>
    /// <returns>Success message including the original filename.</returns>
    Task<string> IngestFileAsync(Stream fileStream, string originalFileName, string chunkingMode = "nlp");

    /// <summary>
    /// Ingests content from a URL: fetches, extracts text, splits, stores.
    /// </summary>
    /// <param name="url">The URL to fetch and ingest.</param>
    /// <param name="chunkingMode">Chunking mode: "fixed", "nlp", or "smart". Defaults to "nlp".</param>
    /// <returns>Success message including the URL.</returns>
    Task<string> IngestUrlAsync(string url, string chunkingMode = "nlp");
}
