using Microsoft.AspNetCore.Mvc;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class SearchController : ControllerBase
{
    private readonly IPineconeService _pineconeService;

    public SearchController(IPineconeService pineconeService)
    {
        _pineconeService = pineconeService;
    }

    /// <summary>
    /// GET /search -- direct similarity search bypassing the agentic chat loop.
    /// Returns matching chunks with content, source, project, and similarity score.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? query = null,
        [FromQuery] string? project = null,
        [FromQuery] int topK = 8)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { error = "Query is required." });
        }

        // Cap topK at 20
        if (topK > 20)
        {
            topK = 20;
        }

        // Ensure topK is at least 1
        if (topK < 1)
        {
            topK = 1;
        }

        try
        {
            var documents = await _pineconeService.SimilaritySearchAsync(query, topK, project);

            var results = documents.Select(d => new SearchResultItem
            {
                Content = d.PageContent,
                Source = d.Metadata.GetValueOrDefault("source", string.Empty),
                Project = d.Metadata.TryGetValue("project", out var p) && !string.IsNullOrEmpty(p) ? p : null,
                Score = d.Score
            }).ToList();

            var response = new SearchResponse
            {
                Results = results,
                Count = results.Count
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Search failed", detail = ex.Message });
        }
    }
}
