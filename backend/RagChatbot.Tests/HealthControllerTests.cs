using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using RagChatbot.Core.Models;

namespace RagChatbot.Tests;

public class HealthControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthControllerTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealth_ReturnsOkWithStatusOk()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("ok");
    }

    [Fact]
    public async Task GetHealth_ReturnsJsonContentType()
    {
        var response = await _client.GetAsync("/health");

        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }
}
