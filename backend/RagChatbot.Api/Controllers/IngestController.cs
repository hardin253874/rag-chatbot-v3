using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RagChatbot.Core.Interfaces;

namespace RagChatbot.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class IngestController : ControllerBase
{
    private readonly IIngestionService _ingestionService;
    private readonly IPineconeService _pineconeService;

    public IngestController(IIngestionService ingestionService, IPineconeService pineconeService)
    {
        _ingestionService = ingestionService;
        _pineconeService = pineconeService;
    }

    /// <summary>
    /// POST /ingest — handles both file upload (multipart/form-data) and URL ingestion (JSON body).
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data", "application/json")]
    public async Task<IActionResult> Ingest()
    {
        try
        {
            // Check for file upload first (multipart/form-data)
            if (Request.HasFormContentType && Request.Form.Files.Count > 0)
            {
                var file = Request.Form.Files["file"] ?? Request.Form.Files[0];
                if (file.Length > 0)
                {
                    using var stream = file.OpenReadStream();
                    var message = await _ingestionService.IngestFileAsync(stream, file.FileName);
                    return Ok(new { success = true, message });
                }
            }

            // Check for URL in JSON body
            if (Request.ContentType?.Contains("application/json") == true)
            {
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();

                if (!string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(body);
                        if (doc.RootElement.TryGetProperty("url", out var urlElement))
                        {
                            var url = urlElement.GetString();
                            if (!string.IsNullOrWhiteSpace(url))
                            {
                                var message = await _ingestionService.IngestUrlAsync(url);
                                return Ok(new { success = true, message });
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Invalid JSON, fall through to 400
                    }
                }
            }

            return BadRequest(new { error = "No file or URL provided. Upload a file or send a JSON body with a 'url' field." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Ingestion failed", detail = ex.Message });
        }
    }

    /// <summary>
    /// GET /ingest/sources — list unique ingested sources.
    /// </summary>
    [HttpGet("sources")]
    public async Task<IActionResult> GetSources()
    {
        try
        {
            var sources = await _pineconeService.ListSourcesAsync();
            return Ok(new { success = true, sources });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to list sources", detail = ex.Message });
        }
    }

    /// <summary>
    /// DELETE /ingest/reset — clear all data from knowledge base.
    /// </summary>
    [HttpDelete("reset")]
    public async Task<IActionResult> Reset()
    {
        try
        {
            await _pineconeService.ResetCollectionAsync();
            return Ok(new { success = true, message = "Knowledge base cleared." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to reset knowledge base", detail = ex.Message });
        }
    }
}
