using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Tests;

public class ChatControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ChatControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static async IAsyncEnumerable<SseEvent> MockSseEvents()
    {
        yield return new SseEvent { Type = "chunk", Text = "Hello" };
        yield return new SseEvent { Type = "chunk", Text = " world" };
        yield return new SseEvent { Type = "sources", Sources = new List<string> { "doc.md" } };
        yield return new SseEvent { Type = "done" };
        await Task.CompletedTask;
    }

    private HttpClient CreateClientWithMocks(Mock<IRagPipelineService>? ragPipeline = null)
    {
        var mockPipeline = ragPipeline ?? new Mock<IRagPipelineService>();

        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove existing registrations
                var descriptors = services
                    .Where(d => d.ServiceType == typeof(IRagPipelineService)
                             || d.ServiceType == typeof(IConversationalDetector)
                             || d.ServiceType == typeof(ILlmService))
                    .ToList();
                foreach (var d in descriptors)
                    services.Remove(d);

                services.AddSingleton(mockPipeline.Object);
            });
        }).CreateClient();
    }

    [Fact]
    public async Task Post_WithValidQuestion_Returns200WithSseContentType()
    {
        var mockPipeline = new Mock<IRagPipelineService>();
        mockPipeline
            .Setup(p => p.ProcessQueryAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<string?>()))
            .Returns(MockSseEvents());

        var client = CreateClientWithMocks(mockPipeline);

        var request = new { question = "What is RAG?", history = new List<object>() };
        var response = await client.PostAsJsonAsync("/chat", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
    }

    [Fact]
    public async Task Post_WithValidQuestion_StreamsSseEvents()
    {
        var mockPipeline = new Mock<IRagPipelineService>();
        mockPipeline
            .Setup(p => p.ProcessQueryAsync("What is RAG?", It.IsAny<List<ChatMessage>>(), It.IsAny<string?>()))
            .Returns(MockSseEvents());

        var client = CreateClientWithMocks(mockPipeline);

        var request = new { question = "What is RAG?", history = new List<object>() };
        var response = await client.PostAsJsonAsync("/chat", request);

        var body = await response.Content.ReadAsStringAsync();

        // Verify SSE format: data: {...}\n\n
        body.Should().Contain("data: ");
        body.Should().Contain("\"type\":\"chunk\"");
        body.Should().Contain("\"type\":\"sources\"");
        body.Should().Contain("\"type\":\"done\"");
    }

    [Fact]
    public async Task Post_WithMissingQuestion_Returns400()
    {
        var client = CreateClientWithMocks();

        var request = new { question = "", history = new List<object>() };
        var response = await client.PostAsJsonAsync("/chat", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_WithNullQuestion_Returns400()
    {
        var client = CreateClientWithMocks();

        var json = "{\"history\":[]}";
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/chat", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_WithWhitespaceQuestion_Returns400()
    {
        var client = CreateClientWithMocks();

        var request = new { question = "   ", history = new List<object>() };
        var response = await client.PostAsJsonAsync("/chat", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void ChatRequest_Project_DefaultsToNull()
    {
        var request = new ChatRequest();
        request.Project.Should().BeNull();
    }

    [Fact]
    public async Task Post_WithProject_PassesProjectToPipeline()
    {
        var mockPipeline = new Mock<IRagPipelineService>();
        mockPipeline
            .Setup(p => p.ProcessQueryAsync("test", It.IsAny<List<ChatMessage>>(), "NESA"))
            .Returns(MockSseEvents());

        var client = CreateClientWithMocks(mockPipeline);

        var request = new { question = "test", project = "NESA", history = new List<object>() };
        var response = await client.PostAsJsonAsync("/chat", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        mockPipeline.Verify(p => p.ProcessQueryAsync("test", It.IsAny<List<ChatMessage>>(), "NESA"), Times.Once);
    }

    [Fact]
    public async Task Post_WithoutProject_PassesNullProject()
    {
        var mockPipeline = new Mock<IRagPipelineService>();
        mockPipeline
            .Setup(p => p.ProcessQueryAsync("test", It.IsAny<List<ChatMessage>>(), It.IsAny<string?>()))
            .Returns(MockSseEvents());

        var client = CreateClientWithMocks(mockPipeline);

        var request = new { question = "test", history = new List<object>() };
        var response = await client.PostAsJsonAsync("/chat", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        mockPipeline.Verify(p => p.ProcessQueryAsync("test", It.IsAny<List<ChatMessage>>(), null), Times.Once);
    }

    [Fact]
    public async Task Post_SseEventsUseCamelCaseJson()
    {
        var mockPipeline = new Mock<IRagPipelineService>();
        mockPipeline
            .Setup(p => p.ProcessQueryAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<string?>()))
            .Returns(MockSseEvents());

        var client = CreateClientWithMocks(mockPipeline);

        var request = new { question = "test", history = new List<object>() };
        var response = await client.PostAsJsonAsync("/chat", request);
        var body = await response.Content.ReadAsStringAsync();

        // Should use camelCase, not PascalCase
        body.Should().Contain("\"type\":");
        body.Should().Contain("\"text\":");
        body.Should().NotContain("\"Type\":");
        body.Should().NotContain("\"Text\":");
    }
}
