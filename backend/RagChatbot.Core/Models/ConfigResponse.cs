using System.Text.Json.Serialization;

namespace RagChatbot.Core.Models;

public class ConfigResponse
{
    public RewriteLlmConfig RewriteLlm { get; set; } = new();
    public LlmConfig Llm { get; set; } = new();

    /// <summary>
    /// Bot interface info — present only when a "bot" interface binding is configured.
    /// Never contains secrets; "auth" names the header, not a key value.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BotConfig? Bot { get; set; }
}

public class BotConfig
{
    public string Endpoint { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
}

public class RewriteLlmConfig
{
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}

public class LlmConfig
{
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}
