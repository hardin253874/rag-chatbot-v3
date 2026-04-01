using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RagChatbot.Core.Configuration;
using RagChatbot.Core.Interfaces;
using RagChatbot.Infrastructure.Chat;
using RagChatbot.Infrastructure.Chat.Tools;
using RagChatbot.Infrastructure.DocumentProcessing;
using RagChatbot.Infrastructure.Ingestion;
using RagChatbot.Infrastructure.QueryRewrite;
using RagChatbot.Infrastructure.VectorStore;

namespace RagChatbot.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods to register Infrastructure services in the DI container.
/// </summary>
public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, AppConfig config)
    {
        // Document loaders
        services.AddSingleton<IDocumentLoader, TextFileLoader>();
        services.AddHttpClient<IUrlLoader, WebPageLoader>();

        // Text splitters — registered as named/typed services
        // MarkdownSplitter is for .md files, RecursiveCharacterSplitter for .txt and URL content
        services.AddSingleton<MarkdownSplitter>(_ =>
            new MarkdownSplitter(config.ChunkSize, config.ChunkOverlap));
        services.AddSingleton<RecursiveCharacterSplitter>(_ =>
            new RecursiveCharacterSplitter(config.ChunkSize, config.ChunkOverlap));

        // Register the recursive splitter as the default ITextSplitter
        services.AddSingleton<ITextSplitter>(sp =>
            sp.GetRequiredService<RecursiveCharacterSplitter>());

        // Pinecone vector store
        services.AddHttpClient("Pinecone", client =>
        {
            client.BaseAddress = new Uri($"https://{config.PineconeHost}");
        });
        services.AddSingleton<IPineconeService>(sp =>
            new PineconeService(
                sp.GetRequiredService<IHttpClientFactory>(),
                config));

        // Ingestion service
        services.AddSingleton<IIngestionService>(sp =>
            new IngestionService(
                sp.GetRequiredService<IDocumentLoader>(),
                sp.GetRequiredService<IUrlLoader>(),
                sp.GetRequiredService<IPineconeService>(),
                sp.GetRequiredService<MarkdownSplitter>(),
                sp.GetRequiredService<RecursiveCharacterSplitter>()));

        // OpenAI-compatible LLM client (base URL from config, default: https://api.openai.com/v1)
        services.AddHttpClient("OpenAI", client =>
        {
            client.BaseAddress = new Uri(config.LlmBaseUrl.TrimEnd('/') + "/");
        });
        services.AddSingleton<IQueryRewriteService>(sp =>
            new QueryRewriteService(
                sp.GetRequiredService<IHttpClientFactory>(),
                config,
                sp.GetRequiredService<ILogger<QueryRewriteService>>()));

        // LLM service
        services.AddSingleton<ILlmService>(sp =>
            new LlmService(
                sp.GetRequiredService<IHttpClientFactory>(),
                config,
                sp.GetRequiredService<ILogger<LlmService>>()));

        // Agent tools
        services.AddSingleton<SearchKnowledgeBaseTool>(sp =>
            new SearchKnowledgeBaseTool(sp.GetRequiredService<IPineconeService>()));
        services.AddSingleton<ReformulateQueryTool>(sp =>
            new ReformulateQueryTool(sp.GetRequiredService<IQueryRewriteService>()));

        // Agentic RAG pipeline (replaces linear RagPipelineService)
        services.AddSingleton<IRagPipelineService>(sp =>
            new AgenticRagPipelineService(
                sp.GetRequiredService<ILlmService>(),
                sp.GetRequiredService<SearchKnowledgeBaseTool>(),
                sp.GetRequiredService<ReformulateQueryTool>()));

        return services;
    }
}
