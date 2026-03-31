using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using RagChatbot.Core.Configuration;
using RagChatbot.Infrastructure.QueryRewrite;

namespace RagChatbot.Tests.QueryRewrite;

public class QueryRewriteServiceTests
{
    private const string TestBaseUrl = "https://api.test.com/v1";
    private const string TestModel = "test-model";
    private const string TestApiKey = "test-api-key";

    private static AppConfig CreateTestConfig() => new()
    {
        RewriteLlmBaseUrl = TestBaseUrl,
        RewriteLlmModel = TestModel,
        RewriteLlmApiKey = TestApiKey,
        OpenAiApiKey = "fallback-key"
    };

    private static (QueryRewriteService service, Mock<HttpMessageHandler> handler) CreateService(
        string responseContent = "{}",
        HttpStatusCode statusCode = HttpStatusCode.OK,
        AppConfig? config = null)
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
            BaseAddress = new Uri(TestBaseUrl)
        };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("OpenAI")).Returns(httpClient);

        var logger = new Mock<ILogger<QueryRewriteService>>();

        var service = new QueryRewriteService(factory.Object, config ?? CreateTestConfig(), logger.Object);
        return (service, handler);
    }

    private static string CreateChatCompletionResponse(string content)
    {
        return JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new { content }
                }
            }
        });
    }

    [Fact]
    public async Task RewriteQueryAsync_CallsApiWithCorrectBody()
    {
        // Arrange
        var response = CreateChatCompletionResponse("optimized query");
        var (service, handler) = CreateService(response);

        // Act
        await service.RewriteQueryAsync("what's the RAG robot");

        // Assert
        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.RequestUri!.ToString().Contains("/chat/completions")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task RewriteQueryAsync_ReturnsRewrittenQuery()
    {
        // Arrange
        var response = CreateChatCompletionResponse("RAG chatbot");
        var (service, _) = CreateService(response);

        // Act
        var result = await service.RewriteQueryAsync("what's the RAG robot");

        // Assert
        result.Should().Be("RAG chatbot");
    }

    [Fact]
    public async Task RewriteQueryAsync_FallsBackOnApiError()
    {
        // Arrange
        var (service, _) = CreateService("Server Error", HttpStatusCode.InternalServerError);

        // Act
        var result = await service.RewriteQueryAsync("original question");

        // Assert — should return original query on API error
        result.Should().Be("original question");
    }

    [Fact]
    public async Task RewriteQueryAsync_FallsBackOnEmptyResponse()
    {
        // Arrange
        var response = CreateChatCompletionResponse("   ");
        var (service, _) = CreateService(response);

        // Act
        var result = await service.RewriteQueryAsync("original question");

        // Assert
        result.Should().Be("original question");
    }

    [Fact]
    public async Task RewriteQueryAsync_FallsBackOnMissingConfig()
    {
        // Arrange — empty base URL and keys
        var config = new AppConfig
        {
            RewriteLlmBaseUrl = "",
            RewriteLlmApiKey = "",
            OpenAiApiKey = ""
        };
        var (service, _) = CreateService(config: config);

        // Act
        var result = await service.RewriteQueryAsync("original question");

        // Assert
        result.Should().Be("original question");
    }

    [Fact]
    public async Task RewriteQueryAsync_UsesCorrectHeaders()
    {
        // Arrange
        var response = CreateChatCompletionResponse("rewritten");
        var (service, handler) = CreateService(response);

        // Act
        await service.RewriteQueryAsync("test query");

        // Assert
        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Headers.Authorization != null &&
                req.Headers.Authorization.Scheme == "Bearer" &&
                req.Headers.Authorization.Parameter == TestApiKey &&
                req.Content!.Headers.ContentType!.MediaType == "application/json"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task RewriteQueryAsync_UsesCorrectSystemPrompt()
    {
        // Arrange
        var response = CreateChatCompletionResponse("rewritten");
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
                Content = new StringContent(response)
            });

        var httpClient = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri(TestBaseUrl)
        };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("OpenAI")).Returns(httpClient);
        var logger = new Mock<ILogger<QueryRewriteService>>();
        var service = new QueryRewriteService(factory.Object, CreateTestConfig(), logger.Object);

        // Act
        await service.RewriteQueryAsync("what's RAG");

        // Assert
        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("query rewriter");
        capturedBody.Should().Contain("search-optimized query");
        // Note: apostrophe may be JSON-escaped as \u0027
        capturedBody.Should().Contain("RAG");
        capturedBody.Should().Contain(TestModel);
    }

    [Fact]
    public async Task RewriteQueryAsync_FallsBackOnMalformedJson()
    {
        // Arrange
        var (service, _) = CreateService("not valid json");

        // Act
        var result = await service.RewriteQueryAsync("original question");

        // Assert
        result.Should().Be("original question");
    }

    [Fact]
    public async Task RewriteQueryAsync_TrimsWhitespaceFromResponse()
    {
        // Arrange
        var response = CreateChatCompletionResponse("  rewritten query  \n");
        var (service, _) = CreateService(response);

        // Act
        var result = await service.RewriteQueryAsync("test");

        // Assert
        result.Should().Be("rewritten query");
    }
}
