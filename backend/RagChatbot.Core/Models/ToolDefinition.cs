namespace RagChatbot.Core.Models;

/// <summary>
/// Defines a tool that can be offered to the LLM for function calling.
/// </summary>
public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public object ParametersSchema { get; set; } = new();
}
