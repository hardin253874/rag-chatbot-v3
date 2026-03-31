using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RagChatbot.Core.Interfaces;

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

    [Fact]
    public async Task PostIngest_WithFile_ReturnsSuccess()
    {
        // Arrange
        var mockIngestion = new Mock<IIngestionService>();
        mockIngestion.Setup(s => s.IngestFileAsync(It.IsAny<Stream>(), "test.md"))
            .ReturnsAsync("Ingested file: test.md");

        var client = CreateClientWithMocks(ingestionService: mockIngestion);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("# Test content"u8.ToArray());
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "test.md");

        // Act
        var response = await client.PostAsync("/ingest", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("message").GetString().Should().Be("Ingested file: test.md");
    }

    [Fact]
    public async Task PostIngest_WithUrl_ReturnsSuccess()
    {
        // Arrange
        var mockIngestion = new Mock<IIngestionService>();
        mockIngestion.Setup(s => s.IngestUrlAsync("https://example.com"))
            .ReturnsAsync("Ingested URL: https://example.com");

        var client = CreateClientWithMocks(ingestionService: mockIngestion);

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(new { url = "https://example.com" }),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.PostAsync("/ingest", jsonContent);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("message").GetString().Should().Be("Ingested URL: https://example.com");
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
}
