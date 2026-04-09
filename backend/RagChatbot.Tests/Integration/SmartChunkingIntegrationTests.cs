using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RagChatbot.Core.Configuration;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.Chat;
using RagChatbot.Infrastructure.DocumentProcessing;

namespace RagChatbot.Tests.Integration;

/// <summary>
/// Integration test for SmartChunkingSplitter with a real LLM.
/// Skipped when no API key is available.
/// Run with: dotnet test --filter "Category=Integration"
/// </summary>
[Trait("Category", "Integration")]
public class SmartChunkingIntegrationTests
{
    [Fact]
    public void SmartChunking_RealLlm_ProducesSemanticChunks()
    {
        // Skip if no API key is available
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                     ?? Environment.GetEnvironmentVariable("LLM_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            // No API key available -- skip this test silently
            return;
        }

        // Arrange
        var config = new AppConfig
        {
            LlmApiKey = apiKey,
            LlmModel = "gpt-4o-mini",
            LlmBaseUrl = "https://api.openai.com/v1"
        };

        var httpClientFactory = new TestHttpClientFactory(config.LlmBaseUrl);
        var logger = NullLogger<LlmService>.Instance;
        var llmService = new LlmService(httpClientFactory, config, logger);
        var fallbackSplitter = new RecursiveCharacterSplitter(1000, 100);
        var splitter = new SmartChunkingSplitter(llmService, fallbackSplitter);

        var multiTopicDocument = new Document
        {
            PageContent = @"# Introduction to Machine Learning

Machine learning is a subset of artificial intelligence that focuses on building systems that learn from data.
Unlike traditional programming where rules are explicitly coded, ML algorithms improve through experience.

# Types of Machine Learning

There are three main types of machine learning:
1. Supervised Learning - uses labeled data to train models
2. Unsupervised Learning - finds patterns in unlabeled data
3. Reinforcement Learning - learns through trial and error with rewards

# Neural Networks

Neural networks are computing systems inspired by biological neural networks. They consist of layers of
interconnected nodes that process information. Deep learning uses neural networks with many layers to
learn complex patterns in large amounts of data.

# Applications

Machine learning is used in many fields including:
- Healthcare: disease diagnosis, drug discovery
- Finance: fraud detection, algorithmic trading
- Transportation: autonomous vehicles, route optimization
- Natural Language Processing: chatbots, translation",
            Metadata = new Dictionary<string, string> { ["source"] = "ml-overview.md" }
        };

        // Act
        var chunks = splitter.Split(multiTopicDocument);

        // Assert
        chunks.Count.Should().BeGreaterThanOrEqualTo(2, "LLM should produce at least 2 semantic chunks");
        chunks.Count.Should().BeLessThanOrEqualTo(10, "LLM should not produce too many tiny chunks");
        chunks.Should().AllSatisfy(c =>
        {
            c.Content.Should().NotBeNullOrWhiteSpace("each chunk should have content");
            c.Source.Should().Be("ml-overview.md", "source metadata should be preserved");
            c.Id.Should().MatchRegex(@"^doc_\d+_\d+$", "each chunk should have a valid ID");
        });
        chunks.Select(c => c.Id).Should().OnlyHaveUniqueItems("all chunk IDs should be unique");
    }
}

/// <summary>
/// Minimal IHttpClientFactory for integration tests.
/// </summary>
internal class TestHttpClientFactory : IHttpClientFactory
{
    private readonly string _baseUrl;

    public TestHttpClientFactory(string baseUrl)
    {
        _baseUrl = baseUrl;
    }

    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(_baseUrl.TrimEnd('/') + "/")
        };
        return client;
    }
}
