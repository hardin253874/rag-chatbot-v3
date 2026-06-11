using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RagChatbot.Core.Configuration;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Infrastructure.Chat;

/// <summary>
/// ILlmService implementation for the native Anthropic Messages API.
/// Translates the internal OpenAI-style message/tool model into Anthropic's format:
/// - system-role messages → top-level "system" string (never sent as messages)
/// - assistant tool calls → "tool_use" content blocks
/// - tool-role messages → user "tool_result" content blocks
/// - ToolDefinition → tools[] with "input_schema"
/// Always sends "max_tokens" (required); sends "temperature" only when the
/// profile's SupportsTemperature is true (some models reject it).
/// </summary>
public class AnthropicLlmService : ILlmService
{
    private const string AnthropicVersion = "2023-06-01";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LlmProfile _profile;
    private readonly string _apiKey;
    private readonly ILogger<AnthropicLlmService> _logger;

    public AnthropicLlmService(
        IHttpClientFactory httpClientFactory,
        LlmProfile profile,
        string apiKey,
        ILogger<AnthropicLlmService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _profile = profile;
        _apiKey = apiKey;
        _logger = logger;
    }

    private string MessagesUrl => _profile.BaseUrl.TrimEnd('/') + "/v1/messages";

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamCompletionAsync(
        List<ChatMessage> messages,
        float temperature = 0.2f)
    {
        var body = BuildRequestBody(messages, tools: null, temperature, stream: true);
        var json = JsonSerializer.Serialize(body);

        var client = _httpClientFactory.CreateClient("Anthropic");
        using var request = CreateRequest(json);

        using var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(CancellationToken.None);
            _logger.LogError("Anthropic streaming failed with status {StatusCode}: {Error}",
                (int)response.StatusCode, errorBody);
            throw new HttpRequestException(
                $"Anthropic streaming failed with status {(int)response.StatusCode}: {errorBody}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(CancellationToken.None);

            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: "))
                continue;

            var data = line["data: ".Length..];

            var (text, isStop) = ParseStreamEvent(data);
            if (isStop)
                break;

            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }

    /// <inheritdoc />
    public async Task<LlmToolResponse> ChatWithToolsAsync(
        List<ChatMessage> messages,
        List<ToolDefinition> tools,
        float temperature = 0.2f)
    {
        var body = BuildRequestBody(messages, tools, temperature, stream: false);
        var json = JsonSerializer.Serialize(body);

        var client = _httpClientFactory.CreateClient("Anthropic");
        using var request = CreateRequest(json);

        using var response = await client.SendAsync(request, CancellationToken.None);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(CancellationToken.None);
            _logger.LogError("Anthropic tool call failed with status {StatusCode}: {Error}",
                (int)response.StatusCode, errorBody);
            throw new HttpRequestException(
                $"Anthropic tool call failed with status {(int)response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(CancellationToken.None);
        return ParseMessagesResponse(responseJson);
    }

    private HttpRequestMessage CreateRequest(string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private Dictionary<string, object> BuildRequestBody(
        List<ChatMessage> messages,
        List<ToolDefinition>? tools,
        float temperature,
        bool stream)
    {
        var (system, anthropicMessages) = TranslateMessages(messages);

        var body = new Dictionary<string, object>
        {
            ["model"] = _profile.Model,
            ["max_tokens"] = _profile.MaxTokens,
            ["messages"] = anthropicMessages
        };

        if (!string.IsNullOrEmpty(system))
            body["system"] = system;

        if (tools is { Count: > 0 })
        {
            body["tools"] = tools.Select(t => new Dictionary<string, object>
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["input_schema"] = t.ParametersSchema
            }).ToArray();
        }

        // Some Anthropic models reject temperature — only send it when supported.
        if (_profile.SupportsTemperature)
            body["temperature"] = temperature;

        if (stream)
            body["stream"] = true;

        return body;
    }

    /// <summary>
    /// Translates internal ChatMessages into Anthropic message format.
    /// System messages are extracted into the returned system string.
    /// Consecutive tool-role messages are merged into one user message
    /// containing multiple tool_result blocks (Anthropic requires
    /// alternating user/assistant turns).
    /// </summary>
    internal static (string System, List<object> Messages) TranslateMessages(List<ChatMessage> messages)
    {
        var systemParts = new List<string>();
        var result = new List<object>();
        List<object>? pendingToolResults = null;

        void FlushToolResults()
        {
            if (pendingToolResults is { Count: > 0 })
            {
                result.Add(new Dictionary<string, object>
                {
                    ["role"] = "user",
                    ["content"] = pendingToolResults
                });
            }
            pendingToolResults = null;
        }

        foreach (var message in messages)
        {
            if (message.Role == "system")
            {
                systemParts.Add(message.Content);
                continue;
            }

            if (message.Role == "tool")
            {
                pendingToolResults ??= new List<object>();
                pendingToolResults.Add(new Dictionary<string, object>
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = message.ToolCallId ?? string.Empty,
                    ["content"] = message.Content
                });
                continue;
            }

            FlushToolResults();

            if (message.Role == "assistant" && message.ToolCalls is { Count: > 0 })
            {
                var blocks = new List<object>();

                if (!string.IsNullOrEmpty(message.Content))
                {
                    blocks.Add(new Dictionary<string, object>
                    {
                        ["type"] = "text",
                        ["text"] = message.Content
                    });
                }

                foreach (var toolCall in message.ToolCalls)
                {
                    blocks.Add(new Dictionary<string, object>
                    {
                        ["type"] = "tool_use",
                        ["id"] = toolCall.Id,
                        ["name"] = toolCall.Name,
                        ["input"] = ParseToolInput(toolCall.ArgumentsJson)
                    });
                }

                result.Add(new Dictionary<string, object>
                {
                    ["role"] = "assistant",
                    ["content"] = blocks
                });
                continue;
            }

            result.Add(new Dictionary<string, object>
            {
                ["role"] = message.Role,
                ["content"] = message.Content
            });
        }

        FlushToolResults();

        return (string.Join("\n\n", systemParts), result);
    }

    private static object ParseToolInput(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return new Dictionary<string, object>();

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(argumentsJson);
        }
        catch (JsonException)
        {
            return new Dictionary<string, object>();
        }
    }

    private static LlmToolResponse ParseMessagesResponse(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);

        if (!doc.RootElement.TryGetProperty("content", out var contentElement) ||
            contentElement.ValueKind != JsonValueKind.Array)
        {
            return new LlmToolResponse { HasToolCall = false };
        }

        var textBuilder = new StringBuilder();
        var toolCalls = new List<ToolCall>();

        foreach (var block in contentElement.EnumerateArray())
        {
            var type = block.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;

            if (type == "text" && block.TryGetProperty("text", out var textElement))
            {
                textBuilder.Append(textElement.GetString());
            }
            else if (type == "tool_use")
            {
                toolCalls.Add(new ToolCall
                {
                    Id = block.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                    Name = block.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    ArgumentsJson = block.TryGetProperty("input", out var input)
                        ? input.GetRawText()
                        : "{}"
                });
            }
        }

        return new LlmToolResponse
        {
            HasToolCall = toolCalls.Count > 0,
            ToolCalls = toolCalls,
            Content = textBuilder.Length > 0 ? textBuilder.ToString() : null
        };
    }

    /// <summary>
    /// Parses a single SSE data payload from an Anthropic stream.
    /// Returns the text delta (if any) and whether the message has stopped.
    /// </summary>
    private static (string? Text, bool IsStop) ParseStreamEvent(string jsonData)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonData);

            var type = doc.RootElement.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;

            if (type == "message_stop")
                return (null, true);

            if (type == "content_block_delta" &&
                doc.RootElement.TryGetProperty("delta", out var delta) &&
                delta.TryGetProperty("type", out var deltaType) &&
                deltaType.GetString() == "text_delta" &&
                delta.TryGetProperty("text", out var textElement))
            {
                return (textElement.GetString(), false);
            }

            return (null, false);
        }
        catch (JsonException)
        {
            return (null, false);
        }
    }
}
