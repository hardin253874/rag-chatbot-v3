using System.Text.Json.Serialization;

namespace RagChatbot.Core.Models;

/// <summary>
/// Represents a single message in a conversation (user, assistant, system, or tool).
/// </summary>
public class ChatMessage
{
    /// <summary>
    /// The role of the message sender: "user", "assistant", "system", or "tool".
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// The text content of the message.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Tool calls requested by the assistant (only present on assistant messages with tool use).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ToolCall>? ToolCalls { get; set; }

    /// <summary>
    /// The tool call ID this message is responding to (only present on tool role messages).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }
}
