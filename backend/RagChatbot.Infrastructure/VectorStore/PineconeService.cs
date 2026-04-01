using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RagChatbot.Core.Configuration;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Infrastructure.VectorStore;

/// <summary>
/// Pinecone vector store service using the REST API with integrated embeddings.
/// Uses llama-text-embed-v2 via Pinecone's integrated model — no embedding API calls from backend.
/// </summary>
public class PineconeService : IPineconeService
{
    private const int BatchSize = 96;
    private const string PineconeApiVersion = "2025-01";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
    private readonly string _namespace;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PineconeService(IHttpClientFactory httpClientFactory, AppConfig config)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = config.PineconeApiKey;
        _namespace = config.PineconeNamespace;
    }

    /// <inheritdoc />
    public async Task StoreDocumentsAsync(List<DocumentChunk> chunks)
    {
        if (chunks.Count == 0)
            return;

        var batches = chunks
            .Select((chunk, index) => new { chunk, index })
            .GroupBy(x => x.index / BatchSize)
            .Select(g => g.Select(x => x.chunk).ToList())
            .ToList();

        foreach (var batch in batches)
        {
            // Pinecone Records API requires NDJSON (newline-delimited JSON)
            // Each record is a separate JSON object on its own line
            var ndjsonLines = batch.Select(c =>
            {
                var record = new Dictionary<string, object>
                {
                    ["_id"] = c.Id,
                    ["chunk_text"] = c.Content,
                    ["source"] = c.Source
                };
                return JsonSerializer.Serialize(record, JsonOptions);
            });
            var ndjson = string.Join("\n", ndjsonLines);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/records/namespaces/{_namespace}/upsert");
            request.Headers.Add("Api-Key", _apiKey);
            request.Headers.Add("X-Pinecone-API-Version", PineconeApiVersion);
            request.Content = new StringContent(ndjson, Encoding.UTF8, "application/x-ndjson");

            var client = _httpClientFactory.CreateClient("Pinecone");
            using var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException(
                    $"Pinecone upsert failed with status {(int)response.StatusCode}: {errorBody}");
            }
        }
    }

    /// <inheritdoc />
    public async Task<List<Document>> SimilaritySearchAsync(string query, int topK = 5)
    {
        var body = new
        {
            query = new
            {
                top_k = topK,
                inputs = new { text = query }
            },
            fields = new[] { "chunk_text", "source" }
        };

        var json = JsonSerializer.Serialize(body, JsonOptions);

        using var request = CreateRequest(
            HttpMethod.Post,
            $"/records/namespaces/{_namespace}/search",
            json);

        var client = _httpClientFactory.CreateClient("Pinecone");
        using var response = await client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Pinecone search failed with status {(int)response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        return ParseSearchResponse(responseJson);
    }

    /// <inheritdoc />
    public async Task<List<string>> ListSourcesAsync()
    {
        // Use a broad search query to retrieve sources since the list endpoint
        // does not return field values. Practical limit ~100 unique sources.
        var results = await SimilaritySearchAsync("document", topK: 100);

        return results
            .Select(d => d.Metadata.GetValueOrDefault("source", ""))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .OrderBy(s => s)
            .ToList();
    }

    /// <inheritdoc />
    public async Task ResetCollectionAsync()
    {
        var body = new
        {
            deleteAll = true,
            @namespace = _namespace
        };

        var json = JsonSerializer.Serialize(body, JsonOptions);

        using var request = CreateRequest(HttpMethod.Post, "/vectors/delete", json);

        var client = _httpClientFactory.CreateClient("Pinecone");
        using var response = await client.SendAsync(request);

        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Pinecone delete failed with status {(int)response.StatusCode}: {errorBody}");
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, string? jsonBody = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Api-Key", _apiKey);
        request.Headers.Add("X-Pinecone-API-Version", PineconeApiVersion);

        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static List<Document> ParseSearchResponse(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var hits = doc.RootElement
            .GetProperty("result")
            .GetProperty("hits");

        var documents = new List<Document>();

        foreach (var hit in hits.EnumerateArray())
        {
            var fields = hit.GetProperty("fields");
            var chunkText = fields.GetProperty("chunk_text").GetString() ?? string.Empty;
            var source = fields.GetProperty("source").GetString() ?? string.Empty;
            var score = hit.TryGetProperty("_score", out var scoreElement) ? scoreElement.GetDouble() : 0.0;

            documents.Add(new Document
            {
                PageContent = chunkText,
                Metadata = new Dictionary<string, string> { ["source"] = source },
                Score = score
            });
        }

        return documents;
    }
}
