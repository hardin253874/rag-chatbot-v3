using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RagChatbot.Core.Configuration;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Infrastructure.Chat;

/// <summary>
/// Streams chat completions from an OpenAI-compatible API.
/// Reads the SSE response line by line and yields content tokens.
/// Also supports non-streaming calls with tool/function definitions.
/// </summary>
public class LlmService : ILlmService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AppConfig _config;
    private readonly ILogger<LlmService> _logger;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public LlmService(
        IHttpClientFactory httpClientFactory,
        AppConfig config,
        ILogger<LlmService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamCompletionAsync(
        List<ChatMessage> messages,
        float temperature = 0.2f)
    {
        var requestBody = new
        {
            model = _config.LlmModel,
            messages = SerializeMessages(messages),
            temperature,
            stream = true
        };

        var json = JsonSerializer.Serialize(requestBody, SerializerOptions);

        var client = _httpClientFactory.CreateClient("OpenAI");
        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.EffectiveLlmApiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(CancellationToken.None);
            _logger.LogError("LLM streaming failed with status {StatusCode}: {Error}",
                (int)response.StatusCode, errorBody);
            throw new HttpRequestException(
                $"LLM streaming failed with status {(int)response.StatusCode}: {errorBody}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(CancellationToken.None);

            if (string.IsNullOrEmpty(line))
                continue;

            if (!line.StartsWith("data: "))
                continue;

            var data = line["data: ".Length..];

            if (data == "[DONE]")
                break;

            var content = ExtractContent(data);
            if (!string.IsNullOrEmpty(content))
            {
                yield return content;
            }
        }
    }

    /// <inheritdoc />
    public async Task<LlmToolResponse> ChatWithToolsAsync(
        List<ChatMessage> messages,
        List<ToolDefinition> tools,
        float temperature = 0.2f)
    {
        var requestBody = BuildToolCallRequestBody(messages, tools, temperature);
        var json = JsonSerializer.Serialize(requestBody, SerializerOptions);

        var client = _httpClientFactory.CreateClient("OpenAI");
        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.EffectiveLlmApiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, CancellationToken.None);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(CancellationToken.None);
            _logger.LogError("LLM tool call failed with status {StatusCode}: {Error}",
                (int)response.StatusCode, errorBody);
            throw new HttpRequestException(
                $"LLM tool call failed with status {(int)response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(CancellationToken.None);
        return ParseToolCallResponse(responseJson);
    }

    private object BuildToolCallRequestBody(
        List<ChatMessage> messages,
        List<ToolDefinition> tools,
        float temperature)
    {
        var serializedMessages = SerializeMessages(messages);

        if (tools.Count == 0)
        {
            return new
            {
                model = _config.LlmModel,
                messages = serializedMessages,
                temperature,
                stream = false
            };
        }

        var toolsArray = tools.Select(t => new
        {
            type = "function",
            function = new
            {
                name = t.Name,
                description = t.Description,
                parameters = t.ParametersSchema
            }
        }).ToArray();

        return new
        {
            model = _config.LlmModel,
            messages = serializedMessages,
            tools = toolsArray,
            temperature,
            stream = false
        };
    }

    private static object[] SerializeMessages(List<ChatMessage> messages)
    {
        return messages.Select(m =>
        {
            // Handle tool role messages
            if (m.Role == "tool")
            {
                return (object)new
                {
                    role = m.Role,
                    content = m.Content,
                    tool_call_id = m.ToolCallId
                };
            }

            // Handle assistant messages with tool calls
            if (m.Role == "assistant" && m.ToolCalls is { Count: > 0 })
            {
                return (object)new
                {
                    role = m.Role,
                    content = (string?)null,
                    tool_calls = m.ToolCalls.Select(tc => new
                    {
                        id = tc.Id,
                        type = "function",
                        function = new
                        {
                            name = tc.Name,
                            arguments = tc.ArgumentsJson
                        }
                    }).ToArray()
                };
            }

            // Standard message
            return (object)new { role = m.Role, content = m.Content };
        }).ToArray();
    }

    private static LlmToolResponse ParseToolCallResponse(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var choices = doc.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            return new LlmToolResponse { HasToolCall = false };

        var message = choices[0].GetProperty("message");

        // Check for tool_calls
        if (message.TryGetProperty("tool_calls", out var toolCallsElement) &&
            toolCallsElement.ValueKind == JsonValueKind.Array &&
            toolCallsElement.GetArrayLength() > 0)
        {
            var toolCalls = new List<ToolCall>();
            foreach (var tc in toolCallsElement.EnumerateArray())
            {
                var function = tc.GetProperty("function");
                toolCalls.Add(new ToolCall
                {
                    Id = tc.GetProperty("id").GetString() ?? string.Empty,
                    Name = function.GetProperty("name").GetString() ?? string.Empty,
                    ArgumentsJson = function.GetProperty("arguments").GetString() ?? string.Empty
                });
            }

            return new LlmToolResponse
            {
                HasToolCall = true,
                ToolCalls = toolCalls
            };
        }

        // Content response
        var content = message.TryGetProperty("content", out var contentElement)
            ? contentElement.GetString()
            : null;

        return new LlmToolResponse
        {
            HasToolCall = false,
            Content = content
        };
    }

    private static string? ExtractContent(string jsonData)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonData);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
                return null;

            var delta = choices[0].GetProperty("delta");
            if (delta.TryGetProperty("content", out var contentElement))
            {
                return contentElement.GetString();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
