using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Tests;

public class IngestControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public IngestControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithMocks(
        Mock<IIngestionService>? ingestionService = null,
        Mock<IPineconeService>? pineconeService = null)
    {
        var mockIngestion = ingestionService ?? new Mock<IIngestionService>();
        var mockPinecone = pineconeService ?? new Mock<IPineconeService>();

        // Set default pre-check returns only when using a default mock
        if (pineconeService == null)
        {
            mockPinecone.Setup(p => p.DocumentExistsByHashAsync(It.IsAny<string>()))
                .ReturnsAsync(false);
            mockPinecone.Setup(p => p.DocumentExistsBySourceAsync(It.IsAny<string>()))
                .ReturnsAsync(false);
        }

        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove existing registrations
                var ingestionDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIngestionService));
                if (ingestionDescriptor != null) services.Remove(ingestionDescriptor);

                var pineconeDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IPineconeService));
                if (pineconeDescriptor != null) services.Remove(pineconeDescriptor);

                services.AddSingleton(mockIngestion.Object);
                services.AddSingleton(mockPinecone.Object);
            });
        }).CreateClient();
    }

    /// <summary>
    /// Creates an async enumerable that yields the given events.
    /// </summary>
    private static async IAsyncEnumerable<IngestSseEvent> CreateEventsStream(params IngestSseEvent[] events)
    {
        foreach (var evt in events)
        {
            yield return evt;
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PostIngest_WithFile_StreamsSseEvents()
    {
        // Arrange
        var mockIngestion = new Mock<IIngestionService>();
        mockIngestion.Setup(s => s.IngestFileStreamAsync(It.IsAny<Stream>(), "test.md", It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()))
            .Returns(CreateEventsStream(
                new IngestSseEvent { Type = "status", Message = "Loading document..." },
                new IngestSseEvent { Type = "done", Message = "Ingested file: test.md", Chunks = 1 }));

        var client = CreateClientWithMocks(ingestionService: mockIngestion);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("# Test content"u8.ToArray());
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "test.md");

        // Act
        var response = await client.PostAsync("/ingest", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("data:");
        body.Should().Contain("\"type\":\"done\"");
    }

    [Fact]
    public async Task PostIngest_WithUrl_StreamsSseEvents()
    {
        // Arrange
        var mockIngestion = new Mock<IIngestionService>();
        mockIngestion.Setup(s => s.IngestUrlStreamAsync("https://example.com", It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()))
            .Returns(CreateEventsStream(
                new IngestSseEvent { Type = "status", Message = "Loading document..." },
                new IngestSseEvent { Type = "done", Message = "Ingested URL: https://example.com", Chunks = 1 }));

        var client = CreateClientWithMocks(ingestionService: mockIngestion);

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(new { url = "https://example.com" }),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.PostAsync("/ingest", jsonContent);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("data:");
        body.Should().Contain("\"type\":\"done\"");
    }

    [Fact]
    public async Task PostIngest_WithNoInput_Returns400()
    {
        // Arrange
        var client = CreateClientWithMocks();

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(new { }),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.PostAsync("/ingest", jsonContent);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostIngest_SseResponse_ContainsDataLines()
    {
        // Arrange
        var mockIngestion = new Mock<IIngestionService>();
        mockIngestion.Setup(s => s.IngestFileStreamAsync(It.IsAny<Stream>(), "test.md", It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()))
            .Returns(CreateEventsStream(
                new IngestSseEvent { Type = "status", Message = "Loading document..." },
                new IngestSseEvent { Type = "status", Message = "Chunking..." },
                new IngestSseEvent { Type = "done", Message = "Ingested", Chunks = 2 }));

        var client = CreateClientWithMocks(ingestionService: mockIngestion);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("content"u8.ToArray());
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "test.md");

        // Act
        var response = await client.PostAsync("/ingest", content);

        // Assert
        var body = await response.Content.ReadAsStringAsync();
        // Each SSE event is "data: {json}\n\n"
        var dataLines = body.Split('\n')
            .Where(l => l.StartsWith("data:"))
            .ToList();
        dataLines.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetSources_ReturnsSources()
    {
        // Arrange
        var mockPinecone = new Mock<IPineconeService>();
        mockPinecone.Setup(p => p.ListSourcesAsync())
            .ReturnsAsync(new List<string> { "file1.md", "file2.txt", "https://example.com" });

        var client = CreateClientWithMocks(pineconeService: mockPinecone);

        // Act
        var response = await client.GetAsync("/ingest/sources");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var sources = json.RootElement.GetProperty("sources");
        sources.GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task DeleteReset_ReturnsSuccess()
    {
        // Arrange
        var mockPinecone = new Mock<IPineconeService>();
        mockPinecone.Setup(p => p.ResetCollectionAsync())
            .Returns(Task.CompletedTask);

        var client = CreateClientWithMocks(pineconeService: mockPinecone);

        // Act
        var response = await client.DeleteAsync("/ingest/reset");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("message").GetString().Should().Be("Knowledge base cleared.");
    }

    // --- B18: Pre-check and replace tests ---

    [Fact]
    public async Task PostIngest_DuplicateContent_ReturnsJsonNotSse()
    {
        // Arrange
        var mockIngestion = new Mock<IIngestionService>();
        var mockPinecone = new Mock<IPineconeService>();
        mockPinecone.Setup(p => p.DocumentExistsByHashAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        mockPinecone.Setup(p => p.DocumentExistsBySourceAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        var client = CreateClientWithMocks(ingestionService: mockIngestion, pineconeService: mockPinecone);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("Duplicate content"u8.ToArray());
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "test.md");

        // Act
        var response = await client.PostAsync("/ingest", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("status").GetString().Should().Be("duplicate");
        json.RootElement.GetProperty("message").GetString().Should().Contain("already ingested");
    }

    [Fact]
    public async Task PostIngest_ExistingSource_ReturnsExistsJson()
    {
        // Arrange
        var mockIngestion = new Mock<IIngestionService>();
        var mockPinecone = new Mock<IPineconeService>();
        mockPinecone.Setup(p => p.DocumentExistsByHashAsync(It.IsAny<string>()))
            .ReturnsAsync(false);
        mockPinecone.Setup(p => p.DocumentExistsBySourceAsync("test.md"))
            .ReturnsAsync(true);

        var client = CreateClientWithMocks(ingestionService: mockIngestion, pineconeService: mockPinecone);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("Different content"u8.ToArray());
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "test.md");

        // Act
        var response = await client.PostAsync("/ingest", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("status").GetString().Should().Be("exists");
        json.RootElement.GetProperty("source").GetString().Should().Be("test.md");
    }

    [Fact]
    public async Task PostIngest_ReplaceTrue_StreamsSseWithReplacement()
    {
        // Arrange
        var mockIngestion = new Mock<IIngestionService>();
        mockIngestion.Setup(s => s.IngestFileStreamAsync(
                It.IsAny<Stream>(), "test.md", It.IsAny<string>(), true, It.IsAny<string?>()))
            .Returns(CreateEventsStream(
                new IngestSseEvent { Type = "status", Message = "Replacing previous version..." },
                new IngestSseEvent { Type = "done", Message = "Ingested", Chunks = 1 }));

        var mockPinecone = new Mock<IPineconeService>();
        // Pre-check is skipped when replace=true
        var client = CreateClientWithMocks(ingestionService: mockIngestion, pineconeService: mockPinecone);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("New content"u8.ToArray());
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "test.md");

        // Act
        var response = await client.PostAsync("/ingest?replace=true", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"type\":\"done\"");
    }
}
