using RagChatbot.Core.Interfaces;

namespace RagChatbot.Infrastructure.Chat;

/// <summary>
/// Detects conversational follow-up questions by checking for known phrases.
/// If a match is found, the RAG pipeline can skip vector search and answer
/// from conversation history alone.
/// </summary>
public class ConversationalDetector : IConversationalDetector
{
    private static readonly string[] FollowUpPhrases =
    [
        "you just said",
        "you mentioned",
        "summarise",
        "summarize",
        "what did you",
        "previous",
        "last answer",
        "above",
        "repeat"
    ];

    /// <inheritdoc />
    public bool IsFollowUp(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
            return false;

        var lower = question.ToLowerInvariant();

        return FollowUpPhrases.Any(phrase => lower.Contains(phrase));
    }
}
