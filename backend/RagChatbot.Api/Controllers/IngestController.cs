using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class IngestController : ControllerBase
{
    private readonly IIngestionService _ingestionService;
    private readonly IPineconeService _pineconeService;

    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public IngestController(IIngestionService ingestionService, IPineconeService pineconeService)
    {
        _ingestionService = ingestionService;
        _pineconeService = pineconeService;
    }

    /// <summary>
    /// POST /ingest -- handles both file upload (multipart/form-data) and URL ingestion (JSON body).
    /// Returns SSE stream with progress events, or JSON for pre-check responses (duplicate/exists).
    /// Supports optional chunkingMode parameter: "fixed", "nlp" (default), "smart", or "hybrid".
    /// Supports optional replace=true query parameter to replace existing documents.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data", "application/json")]
    public async Task Ingest([FromQuery] bool replace = false)
    {
        try
        {
            // Check for file upload first (multipart/form-data)
            if (Request.HasFormContentType && Request.Form.Files.Count > 0)
            {
                var file = Request.Form.Files["file"] ?? Request.Form.Files[0];
                if (file.Length > 0)
                {
                    var chunkingMode = Request.Form["chunkingMode"].FirstOrDefault() ?? "nlp";
                    var project = Request.Form["project"].FirstOrDefault();

                    // Read content for hash computation
                    byte[] fileBytes;
                    using (var ms = new MemoryStream())
                    {
                        await file.OpenReadStream().CopyToAsync(ms);
                        fileBytes = ms.ToArray();
                    }

                    var contentText = Encoding.UTF8.GetString(fileBytes);
                    var contentHash = ComputeSha256Hash(contentText);

                    // Pre-check: duplicate or existing source
                    if (!replace)
                    {
                        var preCheckResult = await PerformPreCheck(contentHash, file.FileName);
                        if (preCheckResult != null)
                        {
                            Response.ContentType = "application/json";
                            await Response.WriteAsync(JsonSerializer.Serialize(preCheckResult, SseJsonOptions));
                            return;
                        }
                    }

                    using var stream = new MemoryStream(fileBytes);
                    var events = _ingestionService.IngestFileStreamAsync(
                        stream, file.FileName, chunkingMode, replace, contentHash, project);
                    await StreamSseEvents(events);
                    return;
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
                                var chunkingMode = "nlp";
                                if (doc.RootElement.TryGetProperty("chunkingMode", out var modeElement))
                                {
                                    chunkingMode = modeElement.GetString() ?? "nlp";
                                }

                                var urlProject = doc.RootElement.TryGetProperty("project", out var projectElement)
                                    ? projectElement.GetString()
                                    : null;

                                // For URL ingestion, we cannot pre-check without loading first
                                // The pre-check will happen inside the service if needed
                                var events = _ingestionService.IngestUrlStreamAsync(
                                    url, chunkingMode, replace, contentHash: null, project: urlProject);
                                await StreamSseEvents(events);
                                return;
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Invalid JSON, fall through to 400
                    }
                }
            }

            Response.StatusCode = 400;
            Response.ContentType = "application/json";
            await Response.WriteAsync(JsonSerializer.Serialize(
                new { error = "No file or URL provided. Upload a file or send a JSON body with a 'url' field." },
                SseJsonOptions));
        }
        catch (Exception ex)
        {
            Response.StatusCode = 500;
            Response.ContentType = "application/json";
            await Response.WriteAsync(JsonSerializer.Serialize(
                new { error = "Ingestion failed", detail = ex.Message },
                SseJsonOptions));
        }
    }

    /// <summary>
    /// Performs pre-check for duplicate content or existing source.
    /// Returns null if the document is new (proceed with ingestion).
    /// </summary>
    private async Task<object?> PerformPreCheck(string contentHash, string source)
    {
        try
        {
            // Check for duplicate content (same hash)
            if (await _pineconeService.DocumentExistsByHashAsync(contentHash))
            {
                return new { status = "duplicate", message = "Content already ingested (unchanged)" };
            }

            // Check for existing source (different content)
            if (await _pineconeService.DocumentExistsBySourceAsync(source))
            {
                return new { status = "exists", message = $"{source} already exists. Replace?", source };
            }
        }
        catch
        {
            // If pre-check fails, proceed with ingestion
        }

        return null;
    }

    /// <summary>
    /// Streams SSE events from the ingestion service to the HTTP response.
    /// </summary>
    private async Task StreamSseEvents(IAsyncEnumerable<IngestSseEvent> events)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";

        await foreach (var evt in events)
        {
            var json = JsonSerializer.Serialize(evt, SseJsonOptions);
            await Response.WriteAsync($"data: {json}\n\n");
            await Response.Body.FlushAsync();
        }
    }

    /// <summary>
    /// Computes a lowercase hex SHA-256 hash of the given text.
    /// </summary>
    private static string ComputeSha256Hash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// GET /ingest/projects -- list unique project names.
    /// </summary>
    [HttpGet("projects")]
    public async Task<IActionResult> GetProjects()
    {
        try
        {
            var projects = await _pineconeService.ListProjectsAsync();
            return Ok(new { projects });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to list projects", detail = ex.Message });
        }
    }

    /// <summary>
    /// GET /ingest/sources -- list unique ingested sources.
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
    /// DELETE /ingest/reset -- clear all data from knowledge base.
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
