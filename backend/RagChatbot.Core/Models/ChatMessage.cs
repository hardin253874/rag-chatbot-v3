namespace RagChatbot.Core.Models;

/// <summary>
/// Represents a single message in a conversation (user or assistant).
/// </summary>
public class ChatMessage
{
    /// <summary>
    /// The role of the message sender: "user" or "assistant".
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// The text content of the message.
    /// </summary>
    public string Content { get; set; } = string.Empty;
}
