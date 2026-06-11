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

public class AnthropicLlmServiceTests
{
    private const string TestApiKey = "test-anthropic-key";

    private static LlmProfile CreateProfile(bool supportsTemperature = true, int maxTokens = 1024) => new()
    {
        Name = "claude-test",
        Provider = "anthropic",
        BaseUrl = "https://api.anthropic.com",
        Model = "claude-sonnet-4-6",
        ApiKeyEnv = "ANTHROPIC_API_KEY",
        SupportsTemperature = supportsTemperature,
        MaxTokens = maxTokens
    };

    private sealed class Capture
    {
        public string? Body;
        public HttpRequestMessage? Request;
    }

    private static (AnthropicLlmService Service, Capture Capture) CreateService(
        string responseContent,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        LlmProfile? profile = null,
        bool streamResponse = false)
    {
        var capture = new Capture();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capture.Request = req;
                capture.Body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = streamResponse
                    ? new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(responseContent)))
                    : new StringContent(responseContent, Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler.Object);

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Anthropic")).Returns(httpClient);

        var logger = new Mock<ILogger<AnthropicLlmService>>();
        var service = new AnthropicLlmService(
            factory.Object, profile ?? CreateProfile(), TestApiKey, logger.Object);

        return (service, capture);
    }

    private const string SimpleTextResponse =
        """{"id":"msg_1","content":[{"type":"text","text":"The answer is 42."}],"stop_reason":"end_turn"}""";

    // --- Request shape ---

    [Fact]
    public async Task ChatWithToolsAsync_ExtractsSystemMessages_ToTopLevelSystem()
    {
        var (service, capture) = CreateService(SimpleTextResponse);
        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = "You are helpful." },
            new() { Role = "user", Content = "Hi" }
        };

        await service.ChatWithToolsAsync(messages, new List<ToolDefinition>());

        using var doc = JsonDocument.Parse(capture.Body!);
        doc.RootElement.GetProperty("system").GetString().Should().Be("You are helpful.");

        var sent = doc.RootElement.GetProperty("messages");
        sent.GetArrayLength().Should().Be(1);
        sent[0].GetProperty("role").GetString().Should().Be("user",
            because: "system messages must never appear in the messages array");
    }

    [Fact]
    public async Task ChatWithToolsAsync_JoinsMultipleSystemMessages()
    {
        var (service, capture) = CreateService(SimpleTextResponse);
        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = "First." },
            new() { Role = "system", Content = "Second." },
            new() { Role = "user", Content = "Hi" }
        };

        await service.ChatWithToolsAsync(messages, new List<ToolDefinition>());

        using var doc = JsonDocument.Parse(capture.Body!);
        doc.RootElement.GetProperty("system").GetString().Should().Be("First.\n\nSecond.");
    }

    [Fact]
    public async Task ChatWithToolsAsync_TranslatesAssistantToolCalls_ToToolUseBlocks()
    {
        var (service, capture) = CreateService(SimpleTextResponse);
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "Search for X" },
            new()
            {
                Role = "assistant",
                Content = string.Empty,
                ToolCalls = new List<ToolCall>
                {
                    new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"X"}""" }
                }
            },
            new() { Role = "tool", Content = "Found 1 result", ToolCallId = "call_1" }
        };

        await service.ChatWithToolsAsync(messages, new List<ToolDefinition>());

        using var doc = JsonDocument.Parse(capture.Body!);
        var sent = doc.RootElement.GetProperty("messages");
        sent.GetArrayLength().Should().Be(3);

        var assistant = sent[1];
        assistant.GetProperty("role").GetString().Should().Be("assistant");
        var toolUse = assistant.GetProperty("content")[0];
        toolUse.GetProperty("type").GetString().Should().Be("tool_use");
        toolUse.GetProperty("id").GetString().Should().Be("call_1");
        toolUse.GetProperty("name").GetString().Should().Be("search_knowledge_base");
        toolUse.GetProperty("input").GetProperty("query").GetString().Should().Be("X");
    }

    [Fact]
    public async Task ChatWithToolsAsync_TranslatesToolMessages_ToUserToolResultBlocks()
    {
        var (service, capture) = CreateService(SimpleTextResponse);
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "Search" },
            new()
            {
                Role = "assistant",
                Content = string.Empty,
                ToolCalls = new List<ToolCall>
                {
                    new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = """{"query":"a"}""" }
                }
            },
            new() { Role = "tool", Content = "Result text", ToolCallId = "call_1" }
        };

        await service.ChatWithToolsAsync(messages, new List<ToolDefinition>());

        using var doc = JsonDocument.Parse(capture.Body!);
        var sent = doc.RootElement.GetProperty("messages");

        var toolResultMessage = sent[2];
        toolResultMessage.GetProperty("role").GetString().Should().Be("user",
            because: "tool results must be sent as user messages, never as 'tool' role");
        var block = toolResultMessage.GetProperty("content")[0];
        block.GetProperty("type").GetString().Should().Be("tool_result");
        block.GetProperty("tool_use_id").GetString().Should().Be("call_1");
        block.GetProperty("content").GetString().Should().Be("Result text");
    }

    [Fact]
    public async Task ChatWithToolsAsync_MergesConsecutiveToolResults_IntoOneUserMessage()
    {
        var (service, capture) = CreateService(SimpleTextResponse);
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "Search" },
            new()
            {
                Role = "assistant",
                Content = string.Empty,
                ToolCalls = new List<ToolCall>
                {
                    new() { Id = "call_1", Name = "search_knowledge_base", ArgumentsJson = "{}" },
                    new() { Id = "call_2", Name = "reformulate_query", ArgumentsJson = "{}" }
                }
            },
            new() { Role = "tool", Content = "Result 1", ToolCallId = "call_1" },
            new() { Role = "tool", Content = "Result 2", ToolCallId = "call_2" }
        };

        await service.ChatWithToolsAsync(messages, new List<ToolDefinition>());

        using var doc = JsonDocument.Parse(capture.Body!);
        var sent = doc.RootElement.GetProperty("messages");
        sent.GetArrayLength().Should().Be(3);
        sent[2].GetProperty("content").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task ChatWithToolsAsync_TranslatesToolDefinitions_ToInputSchema()
    {
        var (service, capture) = CreateService(SimpleTextResponse);
        var tools = new List<ToolDefinition>
        {
            new()
            {
                Name = "search_knowledge_base",
                Description = "Search the KB",
                ParametersSchema = new
                {
                    type = "object",
                    properties = new { query = new { type = "string" } },
                    required = new[] { "query" }
                }
            }
        };

        await service.ChatWithToolsAsync(
            new List<ChatMessage> { new() { Role = "user", Content = "hi" } }, tools);

        using var doc = JsonDocument.Parse(capture.Body!);
        var sentTools = doc.RootElement.GetProperty("tools");
        sentTools.GetArrayLength().Should().Be(1);
        sentTools[0].GetProperty("name").GetString().Should().Be("search_knowledge_base");
        sentTools[0].GetProperty("description").GetString().Should().Be("Search the KB");
        sentTools[0].GetProperty("input_schema").GetProperty("type").GetString().Should().Be("object");
    }

    [Fact]
    public async Task ChatWithToolsAsync_OmitsTools_WhenEmpty()
    {
        var (service, capture) = CreateService(SimpleTextResponse);

        await service.ChatWithToolsAsync(
            new List<ChatMessage> { new() { Role = "user", Content = "hi" } },
            new List<ToolDefinition>());

        using var doc = JsonDocument.Parse(capture.Body!);
        doc.RootElement.TryGetProperty("tools", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ChatWithToolsAsync_AlwaysSendsMaxTokens()
    {
        var (service, capture) = CreateService(SimpleTextResponse, profile: CreateProfile(maxTokens: 512));

        await service.ChatWithToolsAsync(
            new List<ChatMessage> { new() { Role = "user", Content = "hi" } },
            new List<ToolDefinition>());

        using var doc = JsonDocument.Parse(capture.Body!);
        doc.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(512);
    }

    [Fact]
    public async Task ChatWithToolsAsync_IncludesTemperature_WhenSupported()
    {
        var (service, capture) = CreateService(SimpleTextResponse, profile: CreateProfile(supportsTemperature: true));

        await service.ChatWithToolsAsync(
            new List<ChatMessage> { new() { Role = "user", Content = "hi" } },
            new List<ToolDefinition>(),
            temperature: 0.5f);

        using var doc = JsonDocument.Parse(capture.Body!);
        doc.RootElement.GetProperty("temperature").GetDouble().Should().Be(0.5);
    }

    [Fact]
    public async Task ChatWithToolsAsync_OmitsTemperature_WhenNotSupported()
    {
        var (service, capture) = CreateService(SimpleTextResponse, profile: CreateProfile(supportsTemperature: false));

        await service.ChatWithToolsAsync(
            new List<ChatMessage> { new() { Role = "user", Content = "hi" } },
            new List<ToolDefinition>(),
            temperature: 0.5f);

        using var doc = JsonDocument.Parse(capture.Body!);
        doc.RootElement.TryGetProperty("temperature", out _).Should().BeFalse(
            because: "models like opus-4.8 reject the temperature parameter");
    }

    [Fact]
    public async Task ChatWithToolsAsync_SendsRequiredHeadersAndUrl()
    {
        var (service, capture) = CreateService(SimpleTextResponse);

        await service.ChatWithToolsAsync(
            new List<ChatMessage> { new() { Role = "user", Content = "hi" } },
            new List<ToolDefinition>());

        var request = capture.Request!;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.ToString().Should().Be("https://api.anthropic.com/v1/messages");
        request.Headers.GetValues("x-api-key").Should().ContainSingle().Which.Should().Be(TestApiKey);
        request.Headers.GetValues("anthropic-version").Should().ContainSingle().Which.Should().Be("2023-06-01");
    }

    // --- Response parsing ---

    [Fact]
    public async Task ChatWithToolsAsync_ParsesTextResponse()
    {
        var (service, _) = CreateService(SimpleTextResponse);

        var response = await service.ChatWithToolsAsync(
            new List<ChatMessage> { new() { Role = "user", Content = "hi" } },
            new List<ToolDefinition>());

        response.HasToolCall.Should().BeFalse();
        response.Content.Should().Be("The answer is 42.");
    }

    [Fact]
    public async Task ChatWithToolsAsync_ParsesToolUseResponse()
    {
        var responseJson =
            """{"id":"msg_1","content":[{"type":"text","text":"Let me search."},{"type":"tool_use","id":"toolu_1","name":"search_knowledge_base","input":{"query":"test"}}],"stop_reason":"tool_use"}""";
        var (service, _) = CreateService(responseJson);

        var response = await service.ChatWithToolsAsync(
            new List<ChatMessage> { new() { Role = "user", Content = "hi" } },
            new List<ToolDefinition>());

        response.HasToolCall.Should().BeTrue();
        response.ToolCalls.Should().HaveCount(1);
        response.ToolCalls[0].Id.Should().Be("toolu_1");
        response.ToolCalls[0].Name.Should().Be("search_knowledge_base");
        response.ToolCalls[0].ArgumentsJson.Should().Be("""{"query":"test"}""");
    }

    [Fact]
    public async Task ChatWithToolsAsync_ParsesMultipleToolUseBlocks()
    {
        var responseJson =
            """{"content":[{"type":"tool_use","id":"toolu_1","name":"search_knowledge_base","input":{"query":"a"}},{"type":"tool_use","id":"toolu_2","name":"reformulate_query","input":{"query":"b"}}]}""";
        var (service, _) = CreateService(responseJson);

        var response = await service.ChatWithToolsAsync(
            new List<ChatMessage> { new() { Role = "user", Content = "hi" } },
            new List<ToolDefinition>());

        response.HasToolCall.Should().BeTrue();
        response.ToolCalls.Should().HaveCount(2);
        response.ToolCalls[1].Name.Should().Be("reformulate_query");
    }

    [Fact]
    public async Task ChatWithToolsAsync_ThrowsOnNonSuccessStatus()
    {
        var (service, _) = CreateService(
            """{"error":{"type":"authentication_error"}}""", HttpStatusCode.Unauthorized);

        var act = () => service.ChatWithToolsAsync(
            new List<ChatMessage> { new() { Role = "user", Content = "hi" } },
            new List<ToolDefinition>());

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*401*");
    }

    // --- Streaming ---

    [Fact]
    public async Task StreamCompletionAsync_YieldsTextDeltas()
    {
        var sse =
            "event: message_start\n" +
            """data: {"type":"message_start","message":{"id":"msg_1"}}""" + "\n\n" +
            "event: content_block_delta\n" +
            """data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hello"}}""" + "\n\n" +
            """data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":" world"}}""" + "\n\n" +
            """data: {"type":"message_stop"}""" + "\n\n";

        var (service, _) = CreateService(sse, streamResponse: true);

        var tokens = new List<string>();
        await foreach (var token in service.StreamCompletionAsync(
            new List<ChatMessage> { new() { Role = "user", Content = "hi" } }))
        {
            tokens.Add(token);
        }

        tokens.Should().Equal("Hello", " world");
    }

    [Fact]
    public async Task StreamCompletionAsync_IgnoresNonTextDeltaEvents()
    {
        var sse =
            """data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""" + "\n\n" +
            """data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{}"}}""" + "\n\n" +
            """data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Only"}}""" + "\n\n" +
            """data: {"type":"message_stop"}""" + "\n\n";

        var (service, _) = CreateService(sse, streamResponse: true);

        var tokens = new List<string>();
        await foreach (var token in service.StreamCompletionAsync(
            new List<ChatMessage> { new() { Role = "user", Content = "hi" } }))
        {
            tokens.Add(token);
        }

        tokens.Should().Equal("Only");
    }

    [Fact]
    public async Task StreamCompletionAsync_SetsStreamTrue_AndSendsMaxTokens()
    {
        var sse = """data: {"type":"message_stop"}""" + "\n\n";
        var (service, capture) = CreateService(sse, profile: CreateProfile(maxTokens: 777), streamResponse: true);

        await foreach (var _ in service.StreamCompletionAsync(
            new List<ChatMessage> { new() { Role = "user", Content = "hi" } }))
        {
            // consume
        }

        using var doc = JsonDocument.Parse(capture.Body!);
        doc.RootElement.GetProperty("stream").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(777);
    }

    [Fact]
    public async Task StreamCompletionAsync_ThrowsOnNonSuccessStatus()
    {
        var (service, _) = CreateService("error", HttpStatusCode.InternalServerError, streamResponse: true);

        var act = async () =>
        {
            await foreach (var _ in service.StreamCompletionAsync(
                new List<ChatMessage> { new() { Role = "user", Content = "hi" } }))
            {
                // consume
            }
        };

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
