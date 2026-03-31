using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using RagChatbot.Core.Configuration;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.Chat;

namespace RagChatbot.Tests.Chat;

public class LlmServiceTests
{
    private const string TestApiKey = "test-openai-key";

    private static AppConfig CreateTestConfig() => new()
    {
        OpenAiApiKey = TestApiKey
    };

    private static string CreateSseChunk(string? content)
    {
        if (content is null)
            return "data: [DONE]\n\n";

        var obj = new
        {
            choices = new[]
            {
                new { delta = new { content } }
            }
        };
        return $"data: {JsonSerializer.Serialize(obj)}\n\n";
    }

    private static (LlmService service, Mock<HttpMessageHandler> handler) CreateService(
        string sseResponse,
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
                Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(sseResponse)))
            });

        var httpClient = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri("https://api.openai.com")
        };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("OpenAI")).Returns(httpClient);

        var logger = new Mock<ILogger<LlmService>>();
        var service = new LlmService(factory.Object, config ?? CreateTestConfig(), logger.Object);
        return (service, handler);
    }

    [Fact]
    public async Task StreamCompletionAsync_ParsesSseTokens()
    {
        var sseResponse =
            CreateSseChunk("Hello") +
            CreateSseChunk(" world") +
            CreateSseChunk(null);

        var (service, _) = CreateService(sseResponse);
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "Hi" }
        };

        var tokens = new List<string>();
        await foreach (var token in service.StreamCompletionAsync(messages))
        {
            tokens.Add(token);
        }

        tokens.Should().Equal("Hello", " world");
    }

    [Fact]
    public async Task StreamCompletionAsync_SkipsDoneMarker()
    {
        var sseResponse =
            CreateSseChunk("Token") +
            "data: [DONE]\n\n";

        var (service, _) = CreateService(sseResponse);
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "test" }
        };

        var tokens = new List<string>();
        await foreach (var token in service.StreamCompletionAsync(messages))
        {
            tokens.Add(token);
        }

        tokens.Should().Equal("Token");
    }

    [Fact]
    public async Task StreamCompletionAsync_SkipsEmptyContent()
    {
        var sseResponse =
            CreateSseChunk("") +
            CreateSseChunk("Real") +
            CreateSseChunk(null);

        var (service, _) = CreateService(sseResponse);
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "test" }
        };

        var tokens = new List<string>();
        await foreach (var token in service.StreamCompletionAsync(messages))
        {
            tokens.Add(token);
        }

        tokens.Should().Equal("Real");
    }

    [Fact]
    public async Task StreamCompletionAsync_SendsCorrectRequestBody()
    {
        var sseResponse = CreateSseChunk(null);
        var (service, handler) = CreateService(sseResponse);

        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = "You are helpful." },
            new() { Role = "user", Content = "Hello" }
        };

        await foreach (var _ in service.StreamCompletionAsync(messages, 0.5f))
        {
            // consume
        }

        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.RequestUri!.PathAndQuery == "/v1/chat/completions" &&
                req.Headers.Authorization!.Scheme == "Bearer" &&
                req.Headers.Authorization!.Parameter == TestApiKey),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task StreamCompletionAsync_SendsModelAndStreamFlag()
    {
        var sseResponse = CreateSseChunk(null);

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
                Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(sseResponse)))
            });

        var httpClient = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri("https://api.openai.com")
        };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("OpenAI")).Returns(httpClient);
        var logger = new Mock<ILogger<LlmService>>();

        var service = new LlmService(factory.Object, CreateTestConfig(), logger.Object);
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "test" }
        };

        await foreach (var _ in service.StreamCompletionAsync(messages))
        {
            // consume
        }

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("model").GetString().Should().Be("gpt-4o-mini");
        doc.RootElement.GetProperty("stream").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("temperature").GetDouble().Should().Be(0.2);
    }

    [Fact]
    public async Task StreamCompletionAsync_ThrowsOnHttpError()
    {
        var (service, _) = CreateService("error", HttpStatusCode.InternalServerError);
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "test" }
        };

        var act = async () =>
        {
            await foreach (var _ in service.StreamCompletionAsync(messages))
            {
                // consume
            }
        };

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task StreamCompletionAsync_SkipsEmptyDataLines()
    {
        // Include blank lines and non-data lines that should be skipped
        var sseResponse =
            "\n" +
            ": comment\n" +
            CreateSseChunk("Valid") +
            "\n" +
            CreateSseChunk(null);

        var (service, _) = CreateService(sseResponse);
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "test" }
        };

        var tokens = new List<string>();
        await foreach (var token in service.StreamCompletionAsync(messages))
        {
            tokens.Add(token);
        }

        tokens.Should().Equal("Valid");
    }
}
