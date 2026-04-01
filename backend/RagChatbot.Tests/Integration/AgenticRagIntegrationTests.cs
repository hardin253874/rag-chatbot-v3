using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RagChatbot.Tests.Integration;

/// <summary>
/// Integration smoke tests for the agentic RAG pipeline.
/// These tests hit real OpenAI and are skipped when no API key is available.
/// Run with: dotnet test --filter "Category=Integration"
/// </summary>
[Trait("Category", "Integration")]
public class AgenticRagIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public AgenticRagIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AgenticPipeline_SmokeTest_StreamsEvents()
    {
        // Skip if no API key is available
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                     ?? Environment.GetEnvironmentVariable("LLM_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            // No API key available — skip this test silently
            return;
        }

        // Send a question to the agentic pipeline
        var requestBody = new { question = "What is RAG?", history = Array.Empty<object>() };
        var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await _client.PostAsync("/chat", content);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var responseText = await response.Content.ReadAsStringAsync();

        // Parse SSE events
        var events = new List<JsonElement>();
        foreach (var line in responseText.Split('\n'))
        {
            if (line.StartsWith("data: "))
            {
                var json = line["data: ".Length..];
                try
                {
                    events.Add(JsonDocument.Parse(json).RootElement.Clone());
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }
        }

        // Verify at least one chunk event
        var hasChunk = events.Any(e =>
        {
            if (e.TryGetProperty("type", out var t))
                return t.GetString() == "chunk";
            return false;
        });
        hasChunk.Should().BeTrue("expected at least one 'chunk' SSE event");

        // Verify a done event
        var hasDone = events.Any(e =>
        {
            if (e.TryGetProperty("type", out var t))
                return t.GetString() == "done";
            return false;
        });
        hasDone.Should().BeTrue("expected a 'done' SSE event");
    }
}
