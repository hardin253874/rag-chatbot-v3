using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RagChatbot.Core.Configuration;
using RagChatbot.Core.Interfaces;
using RagChatbot.Infrastructure.Chat;
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

        // Query rewrite service
        services.AddHttpClient("OpenAI", client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com");
        });
        services.AddSingleton<IQueryRewriteService>(sp =>
            new QueryRewriteService(
                sp.GetRequiredService<IHttpClientFactory>(),
                config,
                sp.GetRequiredService<ILogger<QueryRewriteService>>()));

        // Chat / RAG pipeline services
        services.AddSingleton<IConversationalDetector, ConversationalDetector>();
        services.AddSingleton<ILlmService>(sp =>
            new LlmService(
                sp.GetRequiredService<IHttpClientFactory>(),
                config,
                sp.GetRequiredService<ILogger<LlmService>>()));
        services.AddSingleton<IRagPipelineService>(sp =>
            new RagPipelineService(
                sp.GetRequiredService<IConversationalDetector>(),
                sp.GetRequiredService<IQueryRewriteService>(),
                sp.GetRequiredService<IPineconeService>(),
                sp.GetRequiredService<ILlmService>()));

        return services;
    }
}
