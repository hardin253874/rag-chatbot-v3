namespace RagChatbot.Core.Interfaces;

/// <summary>
/// Detects whether a user question is a conversational follow-up
/// (referring to prior conversation rather than documents).
/// </summary>
public interface IConversationalDetector
{
    /// <summary>
    /// Returns true if the question contains conversational follow-up phrases
    /// such as "you just said", "summarise", "previous", etc.
    /// </summary>
    bool IsFollowUp(string question);
}
