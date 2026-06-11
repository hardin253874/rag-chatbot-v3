namespace RagChatbot.Api.Middleware;

/// <summary>
/// API key middleware for the bot interface. Validates the X-Api-Key header
/// against the RAG_API_KEY environment variable. Mounted EXCLUSIVELY on /bot/*
/// via UseWhen — no existing route gains authentication.
/// Fails closed: if RAG_API_KEY is not configured, all /bot/* requests are 401.
/// </summary>
public class BotApiKeyMiddleware
{
    private const string ApiKeyHeader = "X-Api-Key";

    private readonly RequestDelegate _next;

    public BotApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var expectedKey = Environment.GetEnvironmentVariable("RAG_API_KEY");
        var providedKey = context.Request.Headers[ApiKeyHeader].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(expectedKey) ||
            string.IsNullOrEmpty(providedKey) ||
            !string.Equals(providedKey, expectedKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
            return;
        }

        await _next(context);
    }
}
