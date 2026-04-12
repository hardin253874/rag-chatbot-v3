using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class ChatController : ControllerBase
{
    private readonly IRagPipelineService _ragPipeline;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ChatController(IRagPipelineService ragPipeline)
    {
        _ragPipeline = ragPipeline;
    }

    /// <summary>
    /// POST /chat — RAG query with SSE streaming response.
    /// </summary>
    [HttpPost]
    public async Task Post([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            Response.StatusCode = 400;
            await Response.WriteAsJsonAsync(new { error = "Missing question" });
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";

        try
        {
            await foreach (var sseEvent in _ragPipeline.ProcessQueryAsync(
                request.Question,
                request.History ?? new List<ChatMessage>(),
                request.Project))
            {
                var json = JsonSerializer.Serialize(sseEvent, JsonOptions);
                await Response.WriteAsync($"data: {json}\n\n");
                await Response.Body.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            var errorEvent = new SseEvent { Type = "chunk", Text = $"Error: {ex.Message}" };
            var errorJson = JsonSerializer.Serialize(errorEvent, JsonOptions);
            await Response.WriteAsync($"data: {errorJson}\n\n");

            var doneEvent = new SseEvent { Type = "done" };
            var doneJson = JsonSerializer.Serialize(doneEvent, JsonOptions);
            await Response.WriteAsync($"data: {doneJson}\n\n");
            await Response.Body.FlushAsync();
        }
    }
}
