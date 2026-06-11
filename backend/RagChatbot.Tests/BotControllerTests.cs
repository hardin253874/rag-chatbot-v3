using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using RagChatbot.Api.Controllers;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Tests;

public class BotControllerTests
{
    private static async IAsyncEnumerable<SseEvent> ToAsyncEnumerable(IEnumerable<SseEvent> events)
    {
        foreach (var sseEvent in events)
        {
            yield return sseEvent;
        }
        await Task.CompletedTask;
    }

    private static BotController CreateController(params SseEvent[] events)
    {
        var pipeline = new Mock<IRagPipelineService>();
        pipeline.Setup(p => p.ProcessQueryAsync(
                It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<string?>()))
            .Returns(ToAsyncEnumerable(events));
        return new BotController(pipeline.Object);
    }

    private static BotAskResponse GetResponse(IActionResult result)
    {
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        return ok.Value.Should().BeOfType<BotAskResponse>().Subject;
    }

    [Fact]
    public async Task Ask_ConcatenatesChunkEventsIntoAnswer()
    {
        var controller = CreateController(
            new SseEvent { Type = "status", Text = "Searching..." },
            new SseEvent { Type = "chunk", Text = "Hello " },
            new SseEvent { Type = "chunk", Text = "world" },
            new SseEvent { Type = "done" });

        var result = await controller.Ask(new BotAskRequest { Question = "hi" });

        GetResponse(result).Answer.Should().Be("Hello world");
    }

    [Fact]
    public async Task Ask_DeduplicatesSources_PreservingOrder()
    {
        var controller = CreateController(
            new SseEvent { Type = "chunk", Text = "answer" },
            new SseEvent { Type = "sources", Sources = new List<string> { "a.pdf", "b.md", "a.pdf" } },
            new SseEvent { Type = "sources", Sources = new List<string> { "b.md", "c.txt" } },
            new SseEvent { Type = "done" });

        var result = await controller.Ask(new BotAskRequest { Question = "hi" });

        GetResponse(result).Sources.Should().Equal("a.pdf", "b.md", "c.txt");
    }

    [Fact]
    public async Task Ask_UsesLastQualityEvent()
    {
        var controller = CreateController(
            new SseEvent { Type = "chunk", Text = "answer" },
            new SseEvent { Type = "quality", Faithfulness = 0.4, ContextRecall = 0.5, Warning = "low" },
            new SseEvent { Type = "quality", Faithfulness = 0.9, ContextRecall = 0.8, Warning = null },
            new SseEvent { Type = "done" });

        var result = await controller.Ask(new BotAskRequest { Question = "hi" });

        var quality = GetResponse(result).Quality;
        quality.Should().NotBeNull();
        quality!.Faithfulness.Should().Be(0.9);
        quality.ContextRecall.Should().Be(0.8);
        quality.Warning.Should().BeNull();
    }

    [Fact]
    public async Task Ask_QualityIsNull_WhenNoQualityEvent()
    {
        var controller = CreateController(
            new SseEvent { Type = "chunk", Text = "conversational answer" },
            new SseEvent { Type = "sources", Sources = new List<string>() },
            new SseEvent { Type = "done" });

        var result = await controller.Ask(new BotAskRequest { Question = "hi" });

        GetResponse(result).Quality.Should().BeNull();
    }

    [Fact]
    public async Task Ask_EmptySourcesEvent_YieldsEmptyList()
    {
        var controller = CreateController(
            new SseEvent { Type = "chunk", Text = "answer" },
            new SseEvent { Type = "sources", Sources = new List<string>() },
            new SseEvent { Type = "done" });

        var result = await controller.Ask(new BotAskRequest { Question = "hi" });

        GetResponse(result).Sources.Should().BeEmpty();
    }

    [Fact]
    public async Task Ask_QualityWarningIsPreserved()
    {
        var controller = CreateController(
            new SseEvent { Type = "chunk", Text = "answer" },
            new SseEvent
            {
                Type = "quality",
                Faithfulness = 0.2,
                ContextRecall = 0.1,
                Warning = "This answer may not be fully grounded in the knowledge base"
            },
            new SseEvent { Type = "done" });

        var result = await controller.Ask(new BotAskRequest { Question = "hi" });

        GetResponse(result).Quality!.Warning
            .Should().Be("This answer may not be fully grounded in the knowledge base");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Ask_BlankQuestion_Returns400WithError(string question)
    {
        var controller = CreateController();

        var result = await controller.Ask(new BotAskRequest { Question = question });

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        JsonSerializer.Serialize(badRequest.Value).Should().Contain("Missing question");
    }

    [Fact]
    public async Task Ask_ReturnsSingleJsonObject_NotSse()
    {
        var controller = CreateController(
            new SseEvent { Type = "chunk", Text = "answer" },
            new SseEvent { Type = "sources", Sources = new List<string> { "doc.pdf" } },
            new SseEvent { Type = "quality", Faithfulness = 0.9, ContextRecall = 0.8 },
            new SseEvent { Type = "done" });

        var result = await controller.Ask(new BotAskRequest { Question = "hi" });

        var response = GetResponse(result);
        response.Answer.Should().Be("answer");
        response.Sources.Should().Equal("doc.pdf");
        response.Quality!.Faithfulness.Should().Be(0.9);
    }

    [Fact]
    public async Task Ask_PassesQuestionHistoryAndProjectToPipeline()
    {
        var pipeline = new Mock<IRagPipelineService>();
        pipeline.Setup(p => p.ProcessQueryAsync(
                It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<string?>()))
            .Returns(ToAsyncEnumerable(new[] { new SseEvent { Type = "done" } }));
        var controller = new BotController(pipeline.Object);

        var history = new List<ChatMessage> { new() { Role = "user", Content = "earlier" } };
        await controller.Ask(new BotAskRequest
        {
            Question = "my question",
            Project = "my-project",
            History = history
        });

        pipeline.Verify(p => p.ProcessQueryAsync("my question", history, "my-project"), Times.Once);
    }

    [Fact]
    public async Task Ask_NullHistory_PassesEmptyList()
    {
        var pipeline = new Mock<IRagPipelineService>();
        pipeline.Setup(p => p.ProcessQueryAsync(
                It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<string?>()))
            .Returns(ToAsyncEnumerable(new[] { new SseEvent { Type = "done" } }));
        var controller = new BotController(pipeline.Object);

        await controller.Ask(new BotAskRequest { Question = "q", History = null });

        pipeline.Verify(p => p.ProcessQueryAsync(
            "q", It.Is<List<ChatMessage>>(h => h.Count == 0), null), Times.Once);
    }
}
