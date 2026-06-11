using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.Chat.Tools;

namespace RagChatbot.Tests.Integration;

/// <summary>
/// Integration tests for the /bot/* interface: X-Api-Key middleware scoping,
/// aggregated JSON response, additive /config bot field, and keyed-DI isolation.
/// The keyed "bot" pipeline is replaced with a stub so no network calls occur.
/// </summary>
public class BotApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestRagApiKey = "test-rag-api-key-12345";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly WebApplicationFactory<Program> _stubbedFactory;

    public BotApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("RAG_API_KEY", TestRagApiKey);

        _factory = factory;
        _stubbedFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Later keyed registration wins — replaces the real bot pipeline.
                services.AddKeyedSingleton<IRagPipelineService>("bot",
                    (_, _) => new StubRagPipelineService());
            });
        });
    }

    private HttpClient CreateBotClient(string? apiKey = TestRagApiKey)
    {
        var client = _stubbedFactory.CreateClient();
        if (apiKey != null)
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }
        return client;
    }

    // --- Auth middleware ---

    [Fact]
    public async Task BotAsk_WithValidKey_IsNotUnauthorized()
    {
        var client = CreateBotClient();

        var response = await client.PostAsJsonAsync("/bot/ask", new { question = "hello" });

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BotAsk_WithValidKey_ReturnsAggregatedJson()
    {
        var client = CreateBotClient();

        var response = await client.PostAsJsonAsync("/bot/ask", new { question = "hello" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json",
            because: "/bot/ask returns one JSON object, not an SSE stream");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("answer").GetString().Should().Be("Hello world");
        json.GetProperty("sources").EnumerateArray().Select(s => s.GetString())
            .Should().Equal("doc.pdf");
        json.GetProperty("quality").GetProperty("faithfulness").GetDouble().Should().Be(0.9);
        json.GetProperty("quality").GetProperty("contextRecall").GetDouble().Should().Be(0.8);
    }

    [Fact]
    public async Task BotAsk_QualityIsExplicitNull_WhenNoQualityEvent()
    {
        var client = CreateBotClient();

        var response = await client.PostAsJsonAsync("/bot/ask", new { question = "conversational" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("quality", out var quality).Should().BeTrue();
        quality.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task BotAsk_MissingKey_Returns401WithError()
    {
        var client = CreateBotClient(apiKey: null);

        var response = await client.PostAsJsonAsync("/bot/ask", new { question = "hello" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("error").GetString().Should().Be("Unauthorized");
    }

    [Fact]
    public async Task BotAsk_InvalidKey_Returns401()
    {
        var client = CreateBotClient(apiKey: "wrong-key");

        var response = await client.PostAsJsonAsync("/bot/ask", new { question = "hello" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BotAsk_BlankQuestion_WithValidKey_Returns400()
    {
        var client = CreateBotClient();

        var response = await client.PostAsJsonAsync("/bot/ask", new { question = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("error").GetString().Should().Be("Missing question");
    }

    // --- Existing routes are NOT gated ---

    [Fact]
    public async Task ExistingConfigRoute_WithoutKey_IsNotGated()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/config");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ExistingHealthRoute_WithoutKey_IsNotGated()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ExistingSearchRoute_WithoutKey_IsNotUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/search?query=test");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    // --- /config additive bot field ---

    [Fact]
    public async Task Config_IncludesBotField_WhenBotBindingConfigured()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/config");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.TryGetProperty("bot", out var bot).Should().BeTrue();
        bot.GetProperty("endpoint").GetString().Should().Be("/bot/ask");
        bot.GetProperty("auth").GetString().Should().Be("X-Api-Key");
    }

    [Fact]
    public async Task Config_WithBotField_StillDoesNotExposeApiKeys()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/config");
        var rawJson = await response.Content.ReadAsStringAsync();

        rawJson.Should().Contain("\"bot\"");
        rawJson.Should().NotContain("apiKey");
        rawJson.Should().NotContain("ApiKey");
        rawJson.Should().NotContain("api_key");
        rawJson.Should().NotContain("ANTHROPIC_API_KEY");
        rawJson.Should().NotContain("RAG_API_KEY");
        rawJson.Should().NotContain(TestRagApiKey);
    }

    [Fact]
    public async Task Config_ExistingFields_Unchanged()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/config");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.TryGetProperty("rewriteLlm", out _).Should().BeTrue();
        json.TryGetProperty("llm", out _).Should().BeTrue();
    }

    // --- Keyed DI isolation ---

    [Fact]
    public void KeyedBotSearchTool_IsDistinctFromDefaultInstance()
    {
        var services = _factory.Services;

        var defaultTool = services.GetRequiredService<SearchKnowledgeBaseTool>();
        var botTool = services.GetRequiredKeyedService<SearchKnowledgeBaseTool>("bot");

        botTool.Should().NotBeSameAs(defaultTool,
            because: "the pipeline mutates CurrentProjectFilter — the bot must own its instance");
    }

    [Fact]
    public void KeyedBotPipeline_IsDistinctFromDefaultPipeline()
    {
        var services = _factory.Services;

        var defaultPipeline = services.GetRequiredService<IRagPipelineService>();
        var botPipeline = services.GetRequiredKeyedService<IRagPipelineService>("bot");

        botPipeline.Should().NotBeSameAs(defaultPipeline);
    }

    [Fact]
    public void KeyedBotLlmService_IsDistinctFromDefaultLlmService()
    {
        var services = _factory.Services;

        var defaultLlm = services.GetRequiredService<ILlmService>();
        var botLlm = services.GetRequiredKeyedService<ILlmService>("bot");

        botLlm.Should().NotBeSameAs(defaultLlm);
    }

    /// <summary>
    /// Deterministic stand-in for the keyed bot pipeline — emits the same SSE
    /// event shapes as AgenticRagPipelineService without any network calls.
    /// </summary>
    private sealed class StubRagPipelineService : IRagPipelineService
    {
        public async IAsyncEnumerable<SseEvent> ProcessQueryAsync(
            string question, List<ChatMessage> history, string? project = null)
        {
            yield return new SseEvent { Type = "status", Text = "Searching knowledge base..." };

            if (question == "conversational")
            {
                yield return new SseEvent { Type = "chunk", Text = "Hi there" };
                yield return new SseEvent { Type = "sources", Sources = new List<string>() };
                yield return new SseEvent { Type = "done" };
                await Task.CompletedTask;
                yield break;
            }

            yield return new SseEvent { Type = "chunk", Text = "Hello " };
            yield return new SseEvent { Type = "chunk", Text = "world" };
            yield return new SseEvent { Type = "sources", Sources = new List<string> { "doc.pdf" } };
            yield return new SseEvent { Type = "quality", Faithfulness = 0.9, ContextRecall = 0.8 };
            yield return new SseEvent { Type = "done" };
            await Task.CompletedTask;
        }
    }
}
