namespace RagChatbot.Core.Configuration;

/// <summary>
/// Strongly-typed configuration for the RAG Chatbot application.
/// Bound from environment variables loaded via DotNetEnv.
/// </summary>
public class AppConfig
{
    public string OpenAiApiKey { get; set; } = string.Empty;
    public string PineconeApiKey { get; set; } = string.Empty;
    public int Port { get; set; } = 3010;
    public string RewriteLlmBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string RewriteLlmModel { get; set; } = "gpt-4o-mini";
    public string RewriteLlmApiKey { get; set; } = string.Empty;
    public int ChunkSize { get; set; } = 1000;
    public int ChunkOverlap { get; set; } = 100;
    public string PineconeHost { get; set; } = "rag-chatbot-v3-y3gph8e.svc.aped-4627-b74a.pinecone.io";
    public string PineconeNamespace { get; set; } = "rag-chatbot";

    /// <summary>
    /// Returns the effective API key for the rewrite LLM.
    /// Falls back to OpenAiApiKey if RewriteLlmApiKey is not set.
    /// </summary>
    public string EffectiveRewriteLlmApiKey =>
        string.IsNullOrWhiteSpace(RewriteLlmApiKey) ? OpenAiApiKey : RewriteLlmApiKey;

    /// <summary>
    /// Binds environment variables to this configuration instance.
    /// Call after loading .env file.
    /// </summary>
    public static AppConfig FromEnvironment()
    {
        var config = new AppConfig
        {
            OpenAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty,
            PineconeApiKey = Environment.GetEnvironmentVariable("PINECONE_API_KEY") ?? string.Empty,
            RewriteLlmBaseUrl = Environment.GetEnvironmentVariable("REWRITE_LLM_BASE_URL") ?? "https://api.openai.com/v1",
            RewriteLlmModel = Environment.GetEnvironmentVariable("REWRITE_LLM_MODEL") ?? "gpt-4o-mini",
            RewriteLlmApiKey = Environment.GetEnvironmentVariable("REWRITE_LLM_API_KEY") ?? string.Empty,
            PineconeHost = Environment.GetEnvironmentVariable("PINECONE_HOST") ?? "rag-chatbot-v3-y3gph8e.svc.aped-4627-b74a.pinecone.io",
            PineconeNamespace = Environment.GetEnvironmentVariable("PINECONE_NAMESPACE") ?? "rag-chatbot",
        };

        if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var port))
            config.Port = port;

        if (int.TryParse(Environment.GetEnvironmentVariable("CHUNK_SIZE"), out var chunkSize))
            config.ChunkSize = chunkSize;

        if (int.TryParse(Environment.GetEnvironmentVariable("CHUNK_OVERLAP"), out var chunkOverlap))
            config.ChunkOverlap = chunkOverlap;

        return config;
    }
}
