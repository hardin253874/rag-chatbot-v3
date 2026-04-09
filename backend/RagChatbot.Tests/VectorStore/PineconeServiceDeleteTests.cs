using System.Net;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Moq.Protected;
using RagChatbot.Core.Configuration;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.VectorStore;

namespace RagChatbot.Tests.VectorStore;

public class PineconeServiceDeleteTests
{
    private const string TestHost = "test-index.svc.pinecone.io";
    private const string TestNamespace = "test-namespace";
    private const string TestApiKey = "test-api-key";

    private static AppConfig CreateTestConfig() => new()
    {
        PineconeApiKey = TestApiKey,
        PineconeHost = TestHost,
        PineconeNamespace = TestNamespace
    };

    private static (PineconeService service, Mock<HttpMessageHandler> handler) CreateService(
        string responseContent = "{}",
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(responseContent)
            });

        var httpClient = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri($"https://{TestHost}")
        };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Pinecone")).Returns(httpClient);

        var service = new PineconeService(factory.Object, CreateTestConfig());
        return (service, handler);
    }

    private static (PineconeService service, Mock<HttpMessageHandler> handler) CreateServiceWithCallback(
        Action<HttpRequestMessage> onRequest,
        string responseContent = "{}",
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => onRequest(req))
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(responseContent)
            });

        var httpClient = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri($"https://{TestHost}")
        };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Pinecone")).Returns(httpClient);

        var service = new PineconeService(factory.Object, CreateTestConfig());
        return (service, handler);
    }

    // --- StoreDocumentsAsync with content_hash ---

    [Fact]
    public async Task StoreDocumentsAsync_IncludesContentHashInNdjson()
    {
        string? capturedBody = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{}")
            });

        var httpClient = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri($"https://{TestHost}")
        };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Pinecone")).Returns(httpClient);

        var service = new PineconeService(factory.Object, CreateTestConfig());
        var chunks = new List<DocumentChunk>
        {
            new() { Id = "doc_1_0", Content = "Hello", Source = "test.md", ContentHash = "abc123" }
        };

        await service.StoreDocumentsAsync(chunks);

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("content_hash").GetString().Should().Be("abc123");
    }

    [Fact]
    public async Task StoreDocumentsAsync_OmitsContentHashWhenEmpty()
    {
        string? capturedBody = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{}")
            });

        var httpClient = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri($"https://{TestHost}")
        };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Pinecone")).Returns(httpClient);

        var service = new PineconeService(factory.Object, CreateTestConfig());
        var chunks = new List<DocumentChunk>
        {
            new() { Id = "doc_1_0", Content = "Hello", Source = "test.md" }
        };

        await service.StoreDocumentsAsync(chunks);

        capturedBody.Should().NotBeNull();
        // ContentHash is empty string by default, so it should not be included
        capturedBody.Should().NotContain("content_hash");
    }

    // --- DeleteBySourceAsync ---

    [Fact]
    public async Task DeleteBySourceAsync_SendsFilteredDeleteRequest()
    {
        string? capturedBody = null;
        HttpRequestMessage? capturedRequest = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedRequest = req;
                capturedBody = await req.Content!.ReadAsStringAsync();
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{}")
            });

        var httpClient = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri($"https://{TestHost}")
        };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Pinecone")).Returns(httpClient);

        var service = new PineconeService(factory.Object, CreateTestConfig());

        await service.DeleteBySourceAsync("report.md");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri!.PathAndQuery.Should().Be("/vectors/delete");

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        var filter = doc.RootElement.GetProperty("filter");
        filter.GetProperty("source").GetProperty("$eq").GetString().Should().Be("report.md");
        doc.RootElement.GetProperty("namespace").GetString().Should().Be(TestNamespace);
    }

    [Fact]
    public async Task DeleteBySourceAsync_ThrowsOnHttpError()
    {
        var (service, _) = CreateService("{\"error\":\"forbidden\"}", HttpStatusCode.Forbidden);

        var act = () => service.DeleteBySourceAsync("report.md");

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Pinecone delete by source failed*");
    }

    // --- DocumentExistsByHashAsync ---

    [Fact]
    public async Task DocumentExistsByHashAsync_HashExists_ReturnsTrue()
    {
        var searchResponse = """
        {
            "result": {
                "hits": [
                    { "_id": "doc_1_0", "_score": 0.9, "fields": { "chunk_text": "content" } }
                ]
            }
        }
        """;
        var (service, _) = CreateService(searchResponse);

        var result = await service.DocumentExistsByHashAsync("abc123hash");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task DocumentExistsByHashAsync_HashNotFound_ReturnsFalse()
    {
        var searchResponse = """{"result":{"hits":[]}}""";
        var (service, _) = CreateService(searchResponse);

        var result = await service.DocumentExistsByHashAsync("nonexistent");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DocumentExistsByHashAsync_SendsCorrectFilter()
    {
        string? capturedBody = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("""{"result":{"hits":[]}}""")
            });

        var httpClient = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri($"https://{TestHost}")
        };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Pinecone")).Returns(httpClient);

        var service = new PineconeService(factory.Object, CreateTestConfig());

        await service.DocumentExistsByHashAsync("abc123hash");

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        var query = doc.RootElement.GetProperty("query");
        query.GetProperty("top_k").GetInt32().Should().Be(1);
        var filter = query.GetProperty("filter");
        filter.GetProperty("content_hash").GetProperty("$eq").GetString().Should().Be("abc123hash");
    }

    // --- DocumentExistsBySourceAsync ---

    [Fact]
    public async Task DocumentExistsBySourceAsync_SourceExists_ReturnsTrue()
    {
        var searchResponse = """
        {
            "result": {
                "hits": [
                    { "_id": "doc_1_0", "_score": 0.9, "fields": { "chunk_text": "content" } }
                ]
            }
        }
        """;
        var (service, _) = CreateService(searchResponse);

        var result = await service.DocumentExistsBySourceAsync("report.md");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task DocumentExistsBySourceAsync_SourceNotFound_ReturnsFalse()
    {
        var searchResponse = """{"result":{"hits":[]}}""";
        var (service, _) = CreateService(searchResponse);

        var result = await service.DocumentExistsBySourceAsync("nonexistent.md");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DocumentExistsBySourceAsync_SendsCorrectFilter()
    {
        string? capturedBody = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("""{"result":{"hits":[]}}""")
            });

        var httpClient = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri($"https://{TestHost}")
        };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Pinecone")).Returns(httpClient);

        var service = new PineconeService(factory.Object, CreateTestConfig());

        await service.DocumentExistsBySourceAsync("report.md");

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        var query = doc.RootElement.GetProperty("query");
        query.GetProperty("top_k").GetInt32().Should().Be(1);
        var filter = query.GetProperty("filter");
        filter.GetProperty("source").GetProperty("$eq").GetString().Should().Be("report.md");
    }
}
