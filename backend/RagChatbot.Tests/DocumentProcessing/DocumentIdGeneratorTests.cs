using FluentAssertions;
using RagChatbot.Infrastructure.DocumentProcessing;

namespace RagChatbot.Tests.DocumentProcessing;

public class DocumentIdGeneratorTests
{
    [Fact]
    public void Generate_ReturnsCorrectFormat()
    {
        var id = DocumentIdGenerator.Generate(0, 1234567890);

        id.Should().Be("doc_1234567890_0");
    }

    [Fact]
    public void Generate_IncludesIndex()
    {
        var id = DocumentIdGenerator.Generate(5, 1000);

        id.Should().Be("doc_1000_5");
    }

    [Fact]
    public void Generate_WithoutTimestamp_UsesCurrentTime()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var id = DocumentIdGenerator.Generate(0);
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        id.Should().StartWith("doc_");
        id.Should().EndWith("_0");

        // Extract timestamp from ID and verify it's within range
        var parts = id.Split('_');
        parts.Should().HaveCount(3);
        var timestamp = long.Parse(parts[1]);
        timestamp.Should().BeGreaterThanOrEqualTo(before);
        timestamp.Should().BeLessThanOrEqualTo(after);
    }

    [Fact]
    public void Generate_SameTimestamp_DifferentIndexes_ProducesUniqueIds()
    {
        var ids = Enumerable.Range(0, 10)
            .Select(i => DocumentIdGenerator.Generate(i, 9999))
            .ToList();

        ids.Should().OnlyHaveUniqueItems();
    }
}
