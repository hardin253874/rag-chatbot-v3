using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Tests;

public class SearchControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SearchControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithMocks(Mock<IPineconeService>? pineconeService = null)
    {
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

                services.AddSingleton(new Mock<IIngestionService>().Object);
                services.AddSingleton(mockPinecone.Object);
            });
        }).CreateClient();
    }

    [Fact]
    public async Task GetSearch_ValidQuery_ReturnsResults()
    {
        // Arrange
        var mockPinecone = new Mock<IPineconeService>();
        mockPinecone.Setup(p => p.SimilaritySearchAsync("test query", 8, null))
            .ReturnsAsync(new List<Document>
            {
                new()
                {
                    PageContent = "chunk text content",
                    Metadata = new Dictionary<string, string>
                    {
                        ["source"] = "document.md",
                        ["project"] = "NESA"
                    },
                    Score = 0.87
                },
                new()
                {
                    PageContent = "another chunk",
                    Metadata = new Dictionary<string, string>
                    {
                        ["source"] = "notes.txt"
                    },
                    Score = 0.82
                }
            });

        var client = CreateClientWithMocks(pineconeService: mockPinecone);

        // Act
        var response = await client.GetAsync("/search?query=test+query");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        doc.RootElement.GetProperty("count").GetInt32().Should().Be(2);

        var results = doc.RootElement.GetProperty("results");
        results.GetArrayLength().Should().Be(2);

        var first = results[0];
        first.GetProperty("content").GetString().Should().Be("chunk text content");
        first.GetProperty("source").GetString().Should().Be("document.md");
        first.GetProperty("project").GetString().Should().Be("NESA");
        first.GetProperty("score").GetDouble().Should().BeApproximately(0.87, 0.01);

        var second = results[1];
        second.GetProperty("content").GetString().Should().Be("another chunk");
        second.GetProperty("source").GetString().Should().Be("notes.txt");
        // project should be null (not present or null in JSON)
        second.GetProperty("project").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetSearch_EmptyQuery_Returns400()
    {
        // Arrange
        var client = CreateClientWithMocks();

        // Act
        var response = await client.GetAsync("/search?query=");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Query is required");
    }

    [Fact]
    public async Task GetSearch_NoQueryParam_Returns400()
    {
        // Arrange
        var client = CreateClientWithMocks();

        // Act
        var response = await client.GetAsync("/search");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetSearch_ProjectFilterApplied()
    {
        // Arrange
        var mockPinecone = new Mock<IPineconeService>();
        mockPinecone.Setup(p => p.SimilaritySearchAsync("test", 8, "NESA"))
            .ReturnsAsync(new List<Document>
            {
                new()
                {
                    PageContent = "filtered result",
                    Metadata = new Dictionary<string, string>
                    {
                        ["source"] = "doc.md",
                        ["project"] = "NESA"
                    },
                    Score = 0.9
                }
            });

        var client = CreateClientWithMocks(pineconeService: mockPinecone);

        // Act
        var response = await client.GetAsync("/search?query=test&project=NESA");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        mockPinecone.Verify(p => p.SimilaritySearchAsync("test", 8, "NESA"), Times.Once);
    }

    [Fact]
    public async Task GetSearch_TopKCappedAt20()
    {
        // Arrange
        var mockPinecone = new Mock<IPineconeService>();
        mockPinecone.Setup(p => p.SimilaritySearchAsync("test", 20, null))
            .ReturnsAsync(new List<Document>());

        var client = CreateClientWithMocks(pineconeService: mockPinecone);

        // Act — request topK=50, should be capped to 20
        var response = await client.GetAsync("/search?query=test&topK=50");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        mockPinecone.Verify(p => p.SimilaritySearchAsync("test", 20, null), Times.Once);
    }

    [Fact]
    public async Task GetSearch_DefaultTopKIs8()
    {
        // Arrange
        var mockPinecone = new Mock<IPineconeService>();
        mockPinecone.Setup(p => p.SimilaritySearchAsync("test", 8, null))
            .ReturnsAsync(new List<Document>());

        var client = CreateClientWithMocks(pineconeService: mockPinecone);

        // Act — no topK specified, should default to 8
        var response = await client.GetAsync("/search?query=test");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        mockPinecone.Verify(p => p.SimilaritySearchAsync("test", 8, null), Times.Once);
    }
}
