using System.Net;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Moq.Protected;
using RagChatbot.Core.Configuration;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.VectorStore;

namespace RagChatbot.Tests.VectorStore;

public class PineconeServiceTests
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

    private static (PineconeService service, Mock<HttpMessageHandler> handler) CreateServiceWithSequentialResponses(
        params (string content, HttpStatusCode status)[] responses)
    {
        var handler = new Mock<HttpMessageHandler>();
        var setup = handler.Protected()
            .SetupSequence<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());

        foreach (var (content, status) in responses)
        {
            setup.ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = status,
                Content = new StringContent(content)
            });
        }

        var httpClient = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri($"https://{TestHost}")
        };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Pinecone")).Returns(httpClient);

        var service = new PineconeService(factory.Object, CreateTestConfig());
        return (service, handler);
    }

    // --- StoreDocumentsAsync Tests ---

    [Fact]
    public async Task StoreDocumentsAsync_SendsCorrectUrl()
    {
        var (service, handler) = CreateService();
        var chunks = new List<DocumentChunk>
        {
            new() { Id = "doc_1_0", Content = "Hello world", Source = "test.md" }
        };

        await service.StoreDocumentsAsync(chunks);

        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.RequestUri!.PathAndQuery == $"/records/namespaces/{TestNamespace}/upsert"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task StoreDocumentsAsync_SendsNdjsonBody()
    {
        string? capturedBody = null;
        string? capturedContentType = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
                capturedContentType = req.Content!.Headers.ContentType?.MediaType;
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
            new() { Id = "doc_1_0", Content = "Hello world", Source = "test.md" },
            new() { Id = "doc_1_1", Content = "Second chunk", Source = "test.md" }
        };

        await service.StoreDocumentsAsync(chunks);

        capturedContentType.Should().Be("application/x-ndjson");
        capturedBody.Should().NotBeNull();

        // NDJSON: each line is a separate JSON object
        var lines = capturedBody!.Split('\n');
        lines.Should().HaveCount(2);

        using var doc0 = JsonDocument.Parse(lines[0]);
        doc0.RootElement.GetProperty("_id").GetString().Should().Be("doc_1_0");
        doc0.RootElement.GetProperty("chunk_text").GetString().Should().Be("Hello world");
        doc0.RootElement.GetProperty("source").GetString().Should().Be("test.md");

        using var doc1 = JsonDocument.Parse(lines[1]);
        doc1.RootElement.GetProperty("_id").GetString().Should().Be("doc_1_1");
        doc1.RootElement.GetProperty("chunk_text").GetString().Should().Be("Second chunk");
    }

    [Fact]
    public async Task StoreDocumentsAsync_BatchesInGroupsOf96()
    {
        // Create 200 chunks — should result in 3 batches (96, 96, 8)
        var chunks = Enumerable.Range(0, 200)
            .Select(i => new DocumentChunk
            {
                Id = $"doc_1_{i}",
                Content = $"Chunk {i}",
                Source = "test.md"
            })
            .ToList();

        // Must return a fresh response for each call to avoid ObjectDisposedException
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage
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

        await service.StoreDocumentsAsync(chunks);

        handler.Protected().Verify("SendAsync", Times.Exactly(3),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.RequestUri!.PathAndQuery.Contains("/upsert")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task StoreDocumentsAsync_EmptyList_DoesNotSendRequest()
    {
        var (service, handler) = CreateService();

        await service.StoreDocumentsAsync(new List<DocumentChunk>());

        handler.Protected().Verify("SendAsync", Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task StoreDocumentsAsync_ThrowsOnHttpError()
    {
        var (service, _) = CreateService("{\"error\":\"bad request\"}", HttpStatusCode.BadRequest);
        var chunks = new List<DocumentChunk>
        {
            new() { Id = "doc_1_0", Content = "test", Source = "test.md" }
        };

        var act = () => service.StoreDocumentsAsync(chunks);

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Pinecone upsert failed*");
    }

    [Fact]
    public async Task StoreDocumentsAsync_ExactlyOneBatch_SendsOnce()
    {
        var chunks = Enumerable.Range(0, 96)
            .Select(i => new DocumentChunk { Id = $"doc_1_{i}", Content = $"Chunk {i}", Source = "test.md" })
            .ToList();

        var (service, handler) = CreateService();

        await service.StoreDocumentsAsync(chunks);

        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    // --- SimilaritySearchAsync Tests ---

    [Fact]
    public async Task SimilaritySearchAsync_SendsCorrectUrl()
    {
        var searchResponse = """
        {
            "result": {
                "hits": []
            }
        }
        """;
        var (service, handler) = CreateService(searchResponse);

        await service.SimilaritySearchAsync("test query");

        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.RequestUri!.PathAndQuery == $"/records/namespaces/{TestNamespace}/search"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SimilaritySearchAsync_SendsCorrectJsonBody()
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

        await service.SimilaritySearchAsync("test query", 5);

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        var query = doc.RootElement.GetProperty("query");
        query.GetProperty("top_k").GetInt32().Should().Be(5);
        query.GetProperty("inputs").GetProperty("text").GetString().Should().Be("test query");

        var fields = doc.RootElement.GetProperty("fields");
        fields.GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task SimilaritySearchAsync_ParsesHitsCorrectly()
    {
        var searchResponse = """
        {
            "result": {
                "hits": [
                    {
                        "_id": "doc_1_0",
                        "_score": 0.95,
                        "fields": {
                            "chunk_text": "First chunk content",
                            "source": "doc1.md"
                        }
                    },
                    {
                        "_id": "doc_1_1",
                        "_score": 0.85,
                        "fields": {
                            "chunk_text": "Second chunk content",
                            "source": "doc2.md"
                        }
                    }
                ]
            }
        }
        """;
        var (service, _) = CreateService(searchResponse);

        var results = await service.SimilaritySearchAsync("test query");

        results.Should().HaveCount(2);
        results[0].PageContent.Should().Be("First chunk content");
        results[0].Metadata["source"].Should().Be("doc1.md");
        results[1].PageContent.Should().Be("Second chunk content");
        results[1].Metadata["source"].Should().Be("doc2.md");
    }

    [Fact]
    public async Task SimilaritySearchAsync_ReturnsEmptyListForNoHits()
    {
        var searchResponse = """{"result":{"hits":[]}}""";
        var (service, _) = CreateService(searchResponse);

        var results = await service.SimilaritySearchAsync("test query");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SimilaritySearchAsync_UsesCustomTopK()
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

        await service.SimilaritySearchAsync("test", 10);

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("query").GetProperty("top_k").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task SimilaritySearchAsync_ThrowsOnHttpError()
    {
        var (service, _) = CreateService("{\"error\":\"unauthorized\"}", HttpStatusCode.Unauthorized);

        var act = () => service.SimilaritySearchAsync("test query");

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Pinecone search failed*");
    }

    // --- ListSourcesAsync Tests ---

    [Fact]
    public async Task ListSourcesAsync_ReturnsDeduplicatedSources()
    {
        var searchResponse = """
        {
            "result": {
                "hits": [
                    { "_id": "doc_1_0", "_score": 0.9, "fields": { "chunk_text": "a", "source": "doc1.md" } },
                    { "_id": "doc_1_1", "_score": 0.8, "fields": { "chunk_text": "b", "source": "doc1.md" } },
                    { "_id": "doc_1_2", "_score": 0.7, "fields": { "chunk_text": "c", "source": "doc2.md" } },
                    { "_id": "doc_1_3", "_score": 0.6, "fields": { "chunk_text": "d", "source": "doc3.md" } }
                ]
            }
        }
        """;
        var (service, _) = CreateService(searchResponse);

        var sources = await service.ListSourcesAsync();

        sources.Should().HaveCount(3);
        sources.Should().Contain("doc1.md");
        sources.Should().Contain("doc2.md");
        sources.Should().Contain("doc3.md");
    }

    [Fact]
    public async Task ListSourcesAsync_ReturnsEmptyForNoHits()
    {
        var searchResponse = """{"result":{"hits":[]}}""";
        var (service, _) = CreateService(searchResponse);

        var sources = await service.ListSourcesAsync();

        sources.Should().BeEmpty();
    }

    [Fact]
    public async Task ListSourcesAsync_UsesTopK100()
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

        await service.ListSourcesAsync();

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("query").GetProperty("top_k").GetInt32().Should().Be(100);
    }

    [Fact]
    public async Task ListSourcesAsync_ThrowsOnHttpError()
    {
        var (service, _) = CreateService("{\"error\":\"server error\"}", HttpStatusCode.InternalServerError);

        var act = () => service.ListSourcesAsync();

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Pinecone search failed*");
    }

    // --- ResetCollectionAsync Tests ---

    [Fact]
    public async Task ResetCollectionAsync_SendsCorrectUrl()
    {
        var (service, handler) = CreateService();

        await service.ResetCollectionAsync();

        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.RequestUri!.PathAndQuery == "/vectors/delete"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task ResetCollectionAsync_SendsCorrectJsonBody()
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

        await service.ResetCollectionAsync();

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("deleteAll").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("namespace").GetString().Should().Be(TestNamespace);
    }

    [Fact]
    public async Task ResetCollectionAsync_ThrowsOnHttpError()
    {
        var (service, _) = CreateService("{\"error\":\"forbidden\"}", HttpStatusCode.Forbidden);

        var act = () => service.ResetCollectionAsync();

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Pinecone delete failed*");
    }

    // --- Project Metadata Tests ---

    [Fact]
    public async Task StoreDocumentsAsync_WithProject_IncludesProjectInNdjson()
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
            new() { Id = "doc_1_0", Content = "Hello", Source = "test.md", Project = "NESA" }
        };

        await service.StoreDocumentsAsync(chunks);

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!.Split('\n')[0]);
        doc.RootElement.GetProperty("project").GetString().Should().Be("NESA");
    }

    [Fact]
    public async Task StoreDocumentsAsync_WithEmptyProject_OmitsProjectFromNdjson()
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
            new() { Id = "doc_1_0", Content = "Hello", Source = "test.md", Project = "" }
        };

        await service.StoreDocumentsAsync(chunks);

        capturedBody.Should().NotBeNull();
        capturedBody!.Should().NotContain("\"project\"");
    }

    [Fact]
    public async Task SimilaritySearchAsync_RequestsProjectField()
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

        await service.SimilaritySearchAsync("test query", 5);

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        var fields = doc.RootElement.GetProperty("fields");
        var fieldValues = new List<string>();
        foreach (var f in fields.EnumerateArray())
            fieldValues.Add(f.GetString()!);
        fieldValues.Should().Contain("project");
    }

    [Fact]
    public async Task SimilaritySearchAsync_WithProjectFilter_AppliesEqFilter()
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

        await service.SimilaritySearchAsync("test query", 5, projectFilter: "NESA");

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        var filter = doc.RootElement.GetProperty("query").GetProperty("filter");
        var projectFilter = filter.GetProperty("project");
        projectFilter.GetProperty("$eq").GetString().Should().Be("NESA");
    }

    [Fact]
    public async Task SimilaritySearchAsync_WithoutProjectFilter_NoFilter()
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

        await service.SimilaritySearchAsync("test query", 5, projectFilter: null);

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        var query = doc.RootElement.GetProperty("query");
        query.TryGetProperty("filter", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SimilaritySearchAsync_ParsesProjectFromResponse()
    {
        var searchResponse = """
        {
            "result": {
                "hits": [
                    {
                        "_id": "doc_1_0",
                        "_score": 0.95,
                        "fields": {
                            "chunk_text": "Content",
                            "source": "doc1.md",
                            "project": "NESA"
                        }
                    }
                ]
            }
        }
        """;
        var (service, _) = CreateService(searchResponse);

        var results = await service.SimilaritySearchAsync("test query");

        results.Should().HaveCount(1);
        results[0].Metadata.Should().ContainKey("project");
        results[0].Metadata["project"].Should().Be("NESA");
    }

    [Fact]
    public async Task ListProjectsAsync_ReturnsDistinctSortedProjects()
    {
        var searchResponse = """
        {
            "result": {
                "hits": [
                    { "_id": "doc_1_0", "_score": 0.9, "fields": { "chunk_text": "a", "source": "doc1.md", "project": "ZETA" } },
                    { "_id": "doc_1_1", "_score": 0.8, "fields": { "chunk_text": "b", "source": "doc1.md", "project": "NESA" } },
                    { "_id": "doc_1_2", "_score": 0.7, "fields": { "chunk_text": "c", "source": "doc2.md", "project": "ZETA" } },
                    { "_id": "doc_1_3", "_score": 0.6, "fields": { "chunk_text": "d", "source": "doc3.md" } }
                ]
            }
        }
        """;
        var (service, _) = CreateService(searchResponse);

        var projects = await service.ListProjectsAsync();

        projects.Should().HaveCount(2);
        projects[0].Should().Be("NESA");
        projects[1].Should().Be("ZETA");
    }

    // --- Header Tests ---

    [Fact]
    public async Task AllRequests_IncludeApiKeyHeader()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedRequest = req;
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

        await service.SimilaritySearchAsync("test");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.GetValues("Api-Key").Should().Contain(TestApiKey);
        capturedRequest.Headers.GetValues("X-Pinecone-API-Version").Should().Contain("2025-01");
    }
}
