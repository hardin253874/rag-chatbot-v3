using RagChatbot.Core.Models;

namespace RagChatbot.Core.Interfaces;

/// <summary>
/// Service for interacting with the Pinecone vector store.
/// Uses Pinecone's integrated embedding model (llama-text-embed-v2)
/// so the backend never calls an embedding API directly.
/// </summary>
public interface IPineconeService
{
    /// <summary>
    /// Upserts document chunks into Pinecone in batches of 96.
    /// Each chunk is stored with _id, chunk_text, and source fields.
    /// </summary>
    Task StoreDocumentsAsync(List<DocumentChunk> chunks);

    /// <summary>
    /// Performs a similarity search using Pinecone's integrated embedding.
    /// Returns up to topK matching documents with PageContent and source metadata.
    /// </summary>
    Task<List<Document>> SimilaritySearchAsync(string query, int topK = 5);

    /// <summary>
    /// Returns a deduplicated list of source strings from stored records.
    /// Uses a broad search query to retrieve sources (practical limit ~100).
    /// </summary>
    Task<List<string>> ListSourcesAsync();

    /// <summary>
    /// Deletes all records in the configured Pinecone namespace.
    /// </summary>
    Task ResetCollectionAsync();
}
