using FluentAssertions;
using Moq;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.Chat;
using RagChatbot.Infrastructure.Chat.Tools;

namespace RagChatbot.Tests.Chat;

public class AgenticRagPipelineServicePromptTests
{
    [Fact]
    public async Task AgentSystemPrompt_ContainsNoCitationInstruction()
    {
        var mockLlm = new Mock<ILlmService>();
        var mockPinecone = new Mock<IPineconeService>();
        var mockRewriter = new Mock<IQueryRewriteService>();

        List<ChatMessage>? capturedMessages = null;

        mockLlm.Setup(l => l.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .Callback<List<ChatMessage>, List<ToolDefinition>, float>((msgs, _, _) => capturedMessages = msgs)
            .ReturnsAsync(new LlmToolResponse { HasToolCall = false, Content = "answer" });

        mockLlm.Setup(l => l.StreamCompletionAsync(It.IsAny<List<ChatMessage>>(), It.IsAny<float>()))
            .Returns(AsyncTokens("answer"));

        var searchTool = new SearchKnowledgeBaseTool(mockPinecone.Object);
        var reformulateTool = new ReformulateQueryTool(mockRewriter.Object);
        var service = new AgenticRagPipelineService(mockLlm.Object, searchTool, reformulateTool);

        await foreach (var _ in service.ProcessQueryAsync("test", new List<ChatMessage>())) { }

        capturedMessages.Should().NotBeNull();
        var systemMessage = capturedMessages!.FirstOrDefault(m => m.Role == "system");
        systemMessage.Should().NotBeNull();
        systemMessage!.Content.Should().Contain("Do NOT include source references");
        systemMessage.Content.Should().Contain("Sources are provided separately to the user");
    }

    private static async IAsyncEnumerable<string> AsyncTokens(params string[] tokens)
    {
        foreach (var token in tokens)
        {
            yield return token;
            await Task.CompletedTask;
        }
    }
}
