using FluentAssertions;
using RagChatbot.Infrastructure.Chat;

namespace RagChatbot.Tests.Chat;

public class ConversationalDetectorTests
{
    private readonly ConversationalDetector _detector = new();

    [Theory]
    [InlineData("Can you repeat what you just said?")]
    [InlineData("What did you mentioned earlier?")]
    [InlineData("You mentioned something about RAG")]
    [InlineData("Can you summarise that?")]
    [InlineData("Please summarize the above")]
    [InlineData("What did you say before?")]
    [InlineData("Tell me about the previous answer")]
    [InlineData("Repeat your last answer")]
    [InlineData("What was above?")]
    public void IsFollowUp_WithConversationalPhrase_ReturnsTrue(string question)
    {
        _detector.IsFollowUp(question).Should().BeTrue();
    }

    [Theory]
    [InlineData("What is RAG?")]
    [InlineData("How does vector search work?")]
    [InlineData("Explain embeddings")]
    [InlineData("Tell me about Pinecone")]
    [InlineData("What are the benefits of chunking?")]
    public void IsFollowUp_WithRegularQuestion_ReturnsFalse(string question)
    {
        _detector.IsFollowUp(question).Should().BeFalse();
    }

    [Theory]
    [InlineData("YOU JUST SAID something")]
    [InlineData("SUMMARISE this")]
    [InlineData("What Did You mean?")]
    public void IsFollowUp_IsCaseInsensitive(string question)
    {
        _detector.IsFollowUp(question).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IsFollowUp_WithEmptyOrWhitespace_ReturnsFalse(string question)
    {
        _detector.IsFollowUp(question).Should().BeFalse();
    }
}
