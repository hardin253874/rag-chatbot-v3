using FluentAssertions;
using Moq;
using RagChatbot.Core.Interfaces;
using RagChatbot.Infrastructure.Chat.Tools;

namespace RagChatbot.Tests.Chat.Tools;

public class ReformulateQueryToolTests
{
    private readonly Mock<IQueryRewriteService> _mockRewriter = new();

    private ReformulateQueryTool CreateTool() => new(_mockRewriter.Object);

    [Fact]
    public async Task ExecuteAsync_ParsesArgsAndFormatsResult()
    {
        _mockRewriter.Setup(r => r.RewriteQueryAsync("original query"))
            .ReturnsAsync("reformulated query");

        var tool = CreateTool();
        var result = await tool.ExecuteAsync("""{"query":"original query","reason":"results were bad"}""");

        result.Should().Contain("""Reformulated query: "reformulated query""");
        result.Should().Contain("Use this reformulated query with search_knowledge_base");
    }

    [Fact]
    public void Definition_MatchesExpectedSchema()
    {
        var tool = CreateTool();

        tool.Name.Should().Be("reformulate_query");
        tool.Definition.Name.Should().Be("reformulate_query");
        tool.Definition.Description.Should().Contain("Reformulate a search query");
    }
}
