using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RagChatbot.Tests;

public class ConfigControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ConfigControllerTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetConfig_ReturnsOkStatus()
    {
        var response = await _client.GetAsync("/config");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetConfig_ContainsRewriteLlmSection()
    {
        var response = await _client.GetAsync("/config");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.TryGetProperty("rewriteLlm", out var rewriteLlm).Should().BeTrue();
        rewriteLlm.TryGetProperty("baseUrl", out _).Should().BeTrue();
        rewriteLlm.TryGetProperty("model", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetConfig_HasDefaultValues()
    {
        var response = await _client.GetAsync("/config");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        var rewriteLlm = json.GetProperty("rewriteLlm");
        rewriteLlm.GetProperty("baseUrl").GetString().Should().Be("https://api.openai.com/v1");
        rewriteLlm.GetProperty("model").GetString().Should().Be("gpt-4o-mini");
    }

    [Fact]
    public async Task GetConfig_DoesNotExposeApiKeys()
    {
        var response = await _client.GetAsync("/config");
        var rawJson = await response.Content.ReadAsStringAsync();

        rawJson.Should().NotContain("apiKey", because: "API keys must never be exposed in /config");
        rawJson.Should().NotContain("ApiKey");
        rawJson.Should().NotContain("api_key");
        rawJson.Should().NotContain("OPENAI_API_KEY");
        rawJson.Should().NotContain("PINECONE_API_KEY");
    }
}
