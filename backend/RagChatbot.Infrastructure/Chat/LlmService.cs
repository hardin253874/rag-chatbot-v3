using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RagChatbot.Core.Configuration;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Infrastructure.Chat;

/// <summary>
/// Streams chat completions from an OpenAI-compatible API.
/// Reads the SSE response line by line and yields content tokens.
/// </summary>
public class LlmService : ILlmService
{
    private const string Model = "gpt-4o-mini";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AppConfig _config;
    private readonly ILogger<LlmService> _logger;

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
            model = Model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            temperature,
            stream = true
        };

        var json = JsonSerializer.Serialize(requestBody);

        var client = _httpClientFactory.CreateClient("OpenAI");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.OpenAiApiKey);
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
