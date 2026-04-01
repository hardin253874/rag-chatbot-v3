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

    public ConfigController(IOptions<AppConfig> config)
    {
        _config = config.Value;
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

        return Ok(response);
    }
}
