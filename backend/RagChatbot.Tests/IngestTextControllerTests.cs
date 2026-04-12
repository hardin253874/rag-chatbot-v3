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

public class IngestTextControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public IngestTextControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithMocks(
        Mock<IIngestionService>? ingestionService = null,
        Mock<IPineconeService>? pineconeService = null)
    {
        var mockIngestion = ingestionService ?? new Mock<IIngestionService>();
        var mockPinecone = pineconeService ?? new Mock<IPineconeService>();

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
                var ingestionDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIngestionService));
                if (ingestionDescriptor != null) services.Remove(ingestionDescriptor);

                var pineconeDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IPineconeService));
                if (pineconeDescriptor != null) services.Remove(pineconeDescriptor);

                services.AddSingleton(mockIngestion.Object);
                services.AddSingleton(mockPinecone.Object);
            });
        }).CreateClient();
    }

    private static async IAsyncEnumerable<IngestSseEvent> CreateEventsStream(params IngestSseEvent[] events)
    {
        foreach (var evt in events)
        {
            yield return evt;
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PostIngestText_ValidRequest_ReturnsSseStream()
    {
        // Arrange
        var mockIngestion = new Mock<IIngestionService>();
        mockIngestion.Setup(s => s.IngestTextStreamAsync(
                "Hello world content", "doc.md", "nlp", false, It.IsAny<string?>(), null))
            .Returns(CreateEventsStream(
                new IngestSseEvent { Type = "status", Message = "Processing text document..." },
                new IngestSseEvent { Type = "done", Message = "Ingested doc.md (3 chunks)", Chunks = 3 }));

        var client = CreateClientWithMocks(ingestionService: mockIngestion);

        var json = JsonSerializer.Serialize(new { content = "Hello world content", source = "doc.md" });
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/ingest/text", httpContent);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("data:");
        body.Should().Contain("\"type\":\"done\"");
        body.Should().Contain("\"chunks\":3");
    }

    [Fact]
    public async Task PostIngestText_EmptyContent_Returns400()
    {
        // Arrange
        var client = CreateClientWithMocks();

        var json = JsonSerializer.Serialize(new { content = "", source = "doc.md" });
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/ingest/text", httpContent);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Content is required");
    }

    [Fact]
    public async Task PostIngestText_EmptySource_Returns400()
    {
        // Arrange
        var client = CreateClientWithMocks();

        var json = JsonSerializer.Serialize(new { content = "Some content", source = "" });
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/ingest/text", httpContent);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Source is required");
    }

    [Fact]
    public async Task PostIngestText_WithProject_PassesProjectToService()
    {
        // Arrange
        var mockIngestion = new Mock<IIngestionService>();
        mockIngestion.Setup(s => s.IngestTextStreamAsync(
                It.IsAny<string>(), "doc.md", "nlp", false, It.IsAny<string?>(), "NESA"))
            .Returns(CreateEventsStream(
                new IngestSseEvent { Type = "done", Message = "Ingested", Chunks = 1 }));

        var client = CreateClientWithMocks(ingestionService: mockIngestion);

        var json = JsonSerializer.Serialize(new { content = "Content here", source = "doc.md", project = "NESA" });
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/ingest/text", httpContent);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        mockIngestion.Verify(s => s.IngestTextStreamAsync(
            It.IsAny<string>(), "doc.md", "nlp", false, It.IsAny<string?>(), "NESA"), Times.Once);
    }

    [Fact]
    public async Task PostIngestText_ChunkingModeDefaultsToNlp()
    {
        // Arrange
        var mockIngestion = new Mock<IIngestionService>();
        mockIngestion.Setup(s => s.IngestTextStreamAsync(
                It.IsAny<string>(), "doc.md", "nlp", false, It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(CreateEventsStream(
                new IngestSseEvent { Type = "done", Message = "Ingested", Chunks = 1 }));

        var client = CreateClientWithMocks(ingestionService: mockIngestion);

        // No chunkingMode specified
        var json = JsonSerializer.Serialize(new { content = "Content", source = "doc.md" });
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/ingest/text", httpContent);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        mockIngestion.Verify(s => s.IngestTextStreamAsync(
            It.IsAny<string>(), "doc.md", "nlp", false, It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task PostIngestText_DuplicateHash_ReturnsJsonResponse()
    {
        // Arrange
        var mockIngestion = new Mock<IIngestionService>();
        var mockPinecone = new Mock<IPineconeService>();
        mockPinecone.Setup(p => p.DocumentExistsByHashAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        mockPinecone.Setup(p => p.DocumentExistsBySourceAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        var client = CreateClientWithMocks(ingestionService: mockIngestion, pineconeService: mockPinecone);

        var json = JsonSerializer.Serialize(new { content = "Duplicate content", source = "doc.md" });
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/ingest/text", httpContent);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetString().Should().Be("duplicate");
        doc.RootElement.GetProperty("message").GetString().Should().Contain("already ingested");
    }
}
