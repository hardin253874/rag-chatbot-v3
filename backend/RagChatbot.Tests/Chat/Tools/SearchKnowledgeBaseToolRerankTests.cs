using System.Net;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Moq.Protected;
using RagChatbot.Core.Configuration;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.Chat.Tools;

namespace RagChatbot.Tests.Chat.Tools;

public class SearchKnowledgeBaseToolRerankTests
{
    private readonly Mock<IPineconeService> _mockPinecone = new();
    private readonly AppConfig _config = new() { PineconeApiKey = "test-api-key" };

    private SearchKnowledgeBaseTool CreateToolWithRerank(HttpResponseMessage responseMessage)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(responseMessage);

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://api.pinecone.io/")
        };

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("PineconeRerank")).Returns(httpClient);

        return new SearchKnowledgeBaseTool(_mockPinecone.Object, factoryMock.Object, _config);
    }

    private List<Document> CreateSampleDocuments(int count)
    {
        return Enumerable.Range(0, count).Select(i => new Document
        {
            PageContent = $"Document content {i}",
            Metadata = new Dictionary<string, string> { ["source"] = $"doc{i}.pdf" },
            Score = 0.9 - (i * 0.05)
        }).ToList();
    }

    [Fact]
    public async Task ExecuteAsync_OverFetchesFromPinecone()
    {
        // topK=5 should fetch topK*2=10 from Pinecone
        _mockPinecone.Setup(p => p.SimilaritySearchAsync("test query", 10, It.IsAny<string?>()))
            .ReturnsAsync(CreateSampleDocuments(10));

        var rerankResponse = new
        {
            data = Enumerable.Range(0, 5).Select(i => new { index = i, score = 0.95 - (i * 0.05), document = new { text = $"Document content {i}" } })
        };
        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(rerankResponse))
        };

        var tool = CreateToolWithRerank(responseMessage);
        await tool.ExecuteAsync("""{"query":"test query","top_k":5}""");

        // Verify Pinecone was called with 10 (topK * 2)
        _mockPinecone.Verify(p => p.SimilaritySearchAsync("test query", 10, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CallsRerankApiWithCorrectPayload()
    {
        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(CreateSampleDocuments(6));

        string? capturedBody = null;
        Uri? capturedUri = null;
        bool hasApiKeyHeader = false;

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedUri = req.RequestUri;
                hasApiKeyHeader = req.Headers.Contains("Api-Key");
                capturedBody = await req.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        data = Enumerable.Range(0, 3).Select(i => new { index = i, score = 0.9, document = new { text = $"Document content {i}" } })
                    }))
                };
            });

        var httpClient = new HttpClient(handlerMock.Object) { BaseAddress = new Uri("https://api.pinecone.io/") };
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("PineconeRerank")).Returns(httpClient);

        var tool = new SearchKnowledgeBaseTool(_mockPinecone.Object, factoryMock.Object, _config);
        await tool.ExecuteAsync("""{"query":"my query","top_k":3}""");

        capturedUri.Should().NotBeNull();
        capturedUri!.ToString().Should().Contain("rerank");
        hasApiKeyHeader.Should().BeTrue();

        capturedBody.Should().NotBeNull();
        var parsed = JsonDocument.Parse(capturedBody!);
        parsed.RootElement.GetProperty("model").GetString().Should().Be("bge-reranker-v2-m3");
        parsed.RootElement.GetProperty("query").GetString().Should().Be("my query");
        parsed.RootElement.GetProperty("top_n").GetInt32().Should().Be(3);
        parsed.RootElement.GetProperty("documents").GetArrayLength().Should().Be(6);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsResultsInRerankOrder()
    {
        var docs = CreateSampleDocuments(4);
        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(docs);

        // Rerank reverses the order: index 3, 2, 1, 0
        var rerankResponse = new
        {
            data = new[]
            {
                new { index = 3, score = 0.99, document = new { text = "Document content 3" } },
                new { index = 0, score = 0.85, document = new { text = "Document content 0" } }
            }
        };

        var tool = CreateToolWithRerank(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(rerankResponse))
        });

        var result = await tool.ExecuteAsync("""{"query":"test","top_k":2}""");

        // First result should be doc3 (highest rerank score)
        result.Should().Contain("[1] (score: 0.99, source: doc3.pdf)");
        result.Should().Contain("[2] (score: 0.85, source: doc0.pdf)");
    }

    [Fact]
    public async Task ExecuteAsync_RerankFails_ReturnsSimilarityResults()
    {
        var docs = CreateSampleDocuments(4);
        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(docs);

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        var httpClient = new HttpClient(handlerMock.Object) { BaseAddress = new Uri("https://api.pinecone.io/") };
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("PineconeRerank")).Returns(httpClient);

        var tool = new SearchKnowledgeBaseTool(_mockPinecone.Object, factoryMock.Object, _config);
        var result = await tool.ExecuteAsync("""{"query":"test","top_k":2}""");

        // Should return original similarity results
        result.Should().Contain("Found 2 results");
        result.Should().Contain("Document content 0");
    }

    [Fact]
    public async Task ExecuteAsync_RerankReturns429_ReturnsSimilarityResults()
    {
        var docs = CreateSampleDocuments(4);
        _mockPinecone.Setup(p => p.SimilaritySearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(docs);

        var tool = CreateToolWithRerank(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("Rate limited")
        });

        var result = await tool.ExecuteAsync("""{"query":"test","top_k":2}""");

        // Should fall back to similarity results
        result.Should().Contain("Found 2 results");
        result.Should().Contain("Document content 0");
    }

    [Fact]
    public async Task ExecuteAsync_NoRerankConfig_ReturnsSimilarityResults()
    {
        // Tool without rerank dependencies (backward compat constructor)
        _mockPinecone.Setup(p => p.SimilaritySearchAsync("test", It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(CreateSampleDocuments(3));

        var tool = new SearchKnowledgeBaseTool(_mockPinecone.Object);
        var result = await tool.ExecuteAsync("""{"query":"test","top_k":3}""");

        result.Should().Contain("Found 3 results");
    }
}
