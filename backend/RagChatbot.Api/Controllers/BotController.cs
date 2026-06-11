using System.Text;
using Microsoft.AspNetCore.Mvc;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Api.Controllers;

/// <summary>
/// Bot/agent interface: POST /bot/ask runs the same agentic RAG pipeline as /chat,
/// but aggregates the SSE events server-side and returns one JSON object.
/// Uses the keyed "bot" pipeline so the bot's LLM profile and tool instances
/// are fully isolated from the web/MCP pipeline.
/// </summary>
[ApiController]
[Route("bot")]
public class BotController : ControllerBase
{
    private readonly IRagPipelineService _ragPipeline;

    public BotController([FromKeyedServices("bot")] IRagPipelineService ragPipeline)
    {
        _ragPipeline = ragPipeline;
    }

    /// <summary>
    /// POST /bot/ask — RAG query with a single aggregated JSON response.
    /// </summary>
    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] BotAskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new { error = "Missing question" });
        }

        var answer = new StringBuilder();
        var sources = new List<string>();
        var seenSources = new HashSet<string>();
        BotQuality? quality = null;

        await foreach (var sseEvent in _ragPipeline.ProcessQueryAsync(
            request.Question,
            request.History ?? new List<ChatMessage>(),
            request.Project))
        {
            switch (sseEvent.Type)
            {
                case "chunk":
                    answer.Append(sseEvent.Text);
                    break;

                case "sources":
                    foreach (var source in sseEvent.Sources ?? new List<string>())
                    {
                        if (seenSources.Add(source))
                            sources.Add(source);
                    }
                    break;

                case "quality":
                    quality = new BotQuality
                    {
                        Faithfulness = sseEvent.Faithfulness,
                        ContextRecall = sseEvent.ContextRecall,
                        Warning = sseEvent.Warning
                    };
                    break;
            }
        }

        return Ok(new BotAskResponse
        {
            Answer = answer.ToString(),
            Sources = sources,
            Quality = quality
        });
    }
}
