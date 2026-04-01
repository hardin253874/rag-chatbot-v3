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
            BaseAddress = new Uri("https://api.openai.com/v1/")
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
            BaseAddress = new Uri("https://api.openai.com/v1/")
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

    // --- ChatWithToolsAsync Tests ---

    private static (LlmService service, Mock<HttpMessageHandler> handler) CreateServiceForToolCall(
        string jsonResponse,
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
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri("https://api.openai.com/v1")
        };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("OpenAI")).Returns(httpClient);

        var logger = new Mock<ILogger<LlmService>>();
        var service = new LlmService(factory.Object, config ?? CreateTestConfig(), logger.Object);
        return (service, handler);
    }

    [Fact]
    public async Task ChatWithToolsAsync_ParsesToolCallResponse()
    {
        var jsonResponse = """
        {
            "choices": [{
                "message": {
                    "role": "assistant",
                    "content": null,
                    "tool_calls": [{
                        "id": "call_123",
                        "type": "function",
                        "function": { "name": "search_knowledge_base", "arguments": "{\"query\":\"test\"}" }
                    }]
                },
                "finish_reason": "tool_calls"
            }]
        }
        """;

        var (service, _) = CreateServiceForToolCall(jsonResponse);
        var messages = new List<ChatMessage> { new() { Role = "user", Content = "test" } };
        var tools = new List<ToolDefinition>
        {
            new() { Name = "search_knowledge_base", Description = "Search", ParametersSchema = new { } }
        };

        var response = await service.ChatWithToolsAsync(messages, tools);

        response.HasToolCall.Should().BeTrue();
        response.ToolCalls.Should().HaveCount(1);
        response.ToolCalls[0].Id.Should().Be("call_123");
        response.ToolCalls[0].Name.Should().Be("search_knowledge_base");
        response.ToolCalls[0].ArgumentsJson.Should().Be("{\"query\":\"test\"}");
    }

    [Fact]
    public async Task ChatWithToolsAsync_ParsesContentResponse()
    {
        var jsonResponse = """
        {
            "choices": [{
                "message": { "role": "assistant", "content": "The answer is 42." },
                "finish_reason": "stop"
            }]
        }
        """;

        var (service, _) = CreateServiceForToolCall(jsonResponse);
        var messages = new List<ChatMessage> { new() { Role = "user", Content = "test" } };
        var tools = new List<ToolDefinition>();

        var response = await service.ChatWithToolsAsync(messages, tools);

        response.HasToolCall.Should().BeFalse();
        response.Content.Should().Be("The answer is 42.");
    }

    [Fact]
    public async Task ChatWithToolsAsync_ParsesMultipleToolCalls()
    {
        var jsonResponse = """
        {
            "choices": [{
                "message": {
                    "role": "assistant",
                    "content": null,
                    "tool_calls": [
                        {
                            "id": "call_1",
                            "type": "function",
                            "function": { "name": "search_knowledge_base", "arguments": "{\"query\":\"first\"}" }
                        },
                        {
                            "id": "call_2",
                            "type": "function",
                            "function": { "name": "reformulate_query", "arguments": "{\"query\":\"second\"}" }
                        }
                    ]
                },
                "finish_reason": "tool_calls"
            }]
        }
        """;

        var (service, _) = CreateServiceForToolCall(jsonResponse);
        var messages = new List<ChatMessage> { new() { Role = "user", Content = "test" } };
        var tools = new List<ToolDefinition>();

        var response = await service.ChatWithToolsAsync(messages, tools);

        response.HasToolCall.Should().BeTrue();
        response.ToolCalls.Should().HaveCount(2);
        response.ToolCalls[0].Name.Should().Be("search_knowledge_base");
        response.ToolCalls[1].Name.Should().Be("reformulate_query");
    }

    [Fact]
    public async Task ChatWithToolsAsync_UsesConfigModel()
    {
        var jsonResponse = """{"choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""";

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
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.openai.com/v1") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("OpenAI")).Returns(httpClient);
        var logger = new Mock<ILogger<LlmService>>();

        var config = new AppConfig { OpenAiApiKey = "key", LlmModel = "custom-model-x" };
        var service = new LlmService(factory.Object, config, logger.Object);

        var messages = new List<ChatMessage> { new() { Role = "user", Content = "test" } };
        await service.ChatWithToolsAsync(messages, new List<ToolDefinition>());

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("model").GetString().Should().Be("custom-model-x");
    }

    [Fact]
    public async Task ChatWithToolsAsync_UsesEffectiveLlmApiKey()
    {
        var jsonResponse = """{"choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""";

        HttpRequestMessage? capturedRequest = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.openai.com/v1") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("OpenAI")).Returns(httpClient);
        var logger = new Mock<ILogger<LlmService>>();

        var config = new AppConfig { OpenAiApiKey = "openai-key", LlmApiKey = "custom-llm-key" };
        var service = new LlmService(factory.Object, config, logger.Object);

        var messages = new List<ChatMessage> { new() { Role = "user", Content = "test" } };
        await service.ChatWithToolsAsync(messages, new List<ToolDefinition>());

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.Authorization!.Parameter.Should().Be("custom-llm-key");
    }
}
