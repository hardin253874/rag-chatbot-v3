namespace RagChatbot.Core.Models;

public class ConfigResponse
{
    public RewriteLlmConfig RewriteLlm { get; set; } = new();
}

public class RewriteLlmConfig
{
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}
