using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RagChatbot.Core.Configuration;
using RagChatbot.Core.Models;

namespace RagChatbot.Api.Controllers;

[ApiController]
[Route("")]
public class ConfigController : ControllerBase
{
    private readonly AppConfig _config;
    private readonly LlmProfileRegistry _profileRegistry;

    public ConfigController(IOptions<AppConfig> config, LlmProfileRegistry profileRegistry)
    {
        _config = config.Value;
        _profileRegistry = profileRegistry;
    }

    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        var response = new ConfigResponse
        {
            RewriteLlm = new RewriteLlmConfig
            {
                BaseUrl = _config.RewriteLlmBaseUrl,
                Model = _config.RewriteLlmModel
            },
            Llm = new LlmConfig
            {
                BaseUrl = _config.LlmBaseUrl,
                Model = _config.LlmModel
            }
        };

        // Additive: advertise the bot interface only when a "bot" binding exists.
        // "X-Api-Key" is the header NAME — no secret values are ever returned.
        if (_profileRegistry.HasBinding("bot"))
        {
            response.Bot = new BotConfig
            {
                Endpoint = "/bot/ask",
                Auth = "X-Api-Key"
            };
        }

        return Ok(response);
    }
}
