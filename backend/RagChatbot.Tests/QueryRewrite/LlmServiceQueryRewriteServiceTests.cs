using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.QueryRewrite;

namespace RagChatbot.Tests.QueryRewrite;

public class LlmServiceQueryRewriteServiceTests
{
    private static LlmServiceQueryRewriteService CreateService(Mock<ILlmService> llmMock) =>
        new(llmMock.Object, Mock.Of<ILogger<LlmServiceQueryRewriteService>>());

    private static Mock<ILlmService> CreateLlmMock(string? content)
    {
        var mock = new Mock<ILlmService>();
        mock.Setup(m => m.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ReturnsAsync(new LlmToolResponse { HasToolCall = false, Content = content });
        return mock;
    }

    [Fact]
    public async Task RewriteQueryAsync_ReturnsTrimmedLlmContent()
    {
        var llm = CreateLlmMock("  rewritten search query \n");
        var service = CreateService(llm);

        var result = await service.RewriteQueryAsync("what is the thing?");

        result.Should().Be("rewritten search query");
    }

    [Fact]
    public async Task RewriteQueryAsync_FallsBackToOriginal_OnException()
    {
        var llm = new Mock<ILlmService>();
        llm.Setup(m => m.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .ThrowsAsync(new HttpRequestException("boom"));
        var service = CreateService(llm);

        var result = await service.RewriteQueryAsync("original query");

        result.Should().Be("original query");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RewriteQueryAsync_FallsBackToOriginal_OnEmptyContent(string? content)
    {
        var llm = CreateLlmMock(content);
        var service = CreateService(llm);

        var result = await service.RewriteQueryAsync("original query");

        result.Should().Be("original query");
    }

    [Fact]
    public async Task RewriteQueryAsync_UsesEmptyToolsAndZeroTemperature()
    {
        var llm = CreateLlmMock("rewritten");
        var service = CreateService(llm);

        await service.RewriteQueryAsync("query");

        llm.Verify(m => m.ChatWithToolsAsync(
            It.IsAny<List<ChatMessage>>(),
            It.Is<List<ToolDefinition>>(t => t.Count == 0),
            0.0f), Times.Once);
    }

    [Fact]
    public async Task RewriteQueryAsync_SendsSystemPromptAndUserQuery()
    {
        List<ChatMessage>? capturedMessages = null;
        var llm = new Mock<ILlmService>();
        llm.Setup(m => m.ChatWithToolsAsync(
                It.IsAny<List<ChatMessage>>(),
                It.IsAny<List<ToolDefinition>>(),
                It.IsAny<float>()))
            .Callback<List<ChatMessage>, List<ToolDefinition>, float>((msgs, _, _) => capturedMessages = msgs)
            .ReturnsAsync(new LlmToolResponse { Content = "rewritten" });
        var service = CreateService(llm);

        await service.RewriteQueryAsync("my question");

        capturedMessages.Should().NotBeNull();
        capturedMessages.Should().HaveCount(2);
        capturedMessages![0].Role.Should().Be("system");
        capturedMessages[0].Content.Should().Contain("query rewriter");
        capturedMessages[1].Role.Should().Be("user");
        capturedMessages[1].Content.Should().Be("my question");
    }
}
