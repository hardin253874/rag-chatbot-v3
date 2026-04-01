namespace RagChatbot.Core.Models;

/// <summary>
/// Represents an LLM response that may contain tool calls or text content.
/// Used by the agent loop to determine next action.
/// </summary>
public class LlmToolResponse
{
    /// <summary>True if the LLM wants to call a tool.</summary>
    public bool HasToolCall { get; set; }

    /// <summary>Tool calls requested by the LLM (if any).</summary>
    public List<ToolCall> ToolCalls { get; set; } = new();

    /// <summary>Text content (if LLM is answering, not calling tools).</summary>
    public string? Content { get; set; }
}

/// <summary>
/// Represents a single tool call requested by the LLM.
/// </summary>
public class ToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = string.Empty;
}
