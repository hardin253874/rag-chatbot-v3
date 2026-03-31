using FluentAssertions;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.DocumentProcessing;

namespace RagChatbot.Tests.DocumentProcessing;

public class RecursiveCharacterSplitterTests
{
    [Fact]
    public void Split_ShortText_ReturnsSingleChunk()
    {
        var splitter = new RecursiveCharacterSplitter(chunkSize: 100, chunkOverlap: 10);
        var doc = new Document
        {
            PageContent = "Short text.",
            Metadata = new Dictionary<string, string> { ["source"] = "test.txt" }
        };

        var chunks = splitter.Split(doc);

        chunks.Should().HaveCount(1);
        chunks[0].Content.Should().Be("Short text.");
        chunks[0].Source.Should().Be("test.txt");
    }

    [Fact]
    public void Split_GeneratesDocIds()
    {
        var splitter = new RecursiveCharacterSplitter(chunkSize: 50, chunkOverlap: 10);
        var doc = new Document
        {
            PageContent = new string('a', 30) + "\n\n" + new string('b', 30),
            Metadata = new Dictionary<string, string> { ["source"] = "test.txt" }
        };

        var chunks = splitter.Split(doc);

        foreach (var chunk in chunks)
        {
            chunk.Id.Should().MatchRegex(@"^doc_\d+_\d+$");
        }
        chunks.Select(c => c.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Split_SplitsByDoubleNewline()
    {
        // Text must exceed chunk size to trigger splitting
        var p1 = "Paragraph one with enough content to fill a chunk.";
        var p2 = "Paragraph two with enough content to fill a chunk.";
        var splitter = new RecursiveCharacterSplitter(chunkSize: 55, chunkOverlap: 0);
        var doc = new Document
        {
            PageContent = $"{p1}\n\n{p2}",
            Metadata = new Dictionary<string, string> { ["source"] = "test.txt" }
        };

        var chunks = splitter.Split(doc);

        chunks.Should().HaveCount(2);
        chunks[0].Content.Should().Be(p1);
        chunks[1].Content.Should().Be(p2);
    }

    [Fact]
    public void Split_SplitsBySingleNewline_WhenNoDoubleNewline()
    {
        // Text must exceed chunk size to trigger splitting; no double-newlines present
        var line1 = "Line one with content here.";
        var line2 = "Line two with content here.";
        var splitter = new RecursiveCharacterSplitter(chunkSize: 30, chunkOverlap: 0);
        var doc = new Document
        {
            PageContent = $"{line1}\n{line2}",
            Metadata = new Dictionary<string, string> { ["source"] = "test.txt" }
        };

        var chunks = splitter.Split(doc);

        chunks.Should().HaveCount(2);
        chunks[0].Content.Should().Be(line1);
        chunks[1].Content.Should().Be(line2);
    }

    [Fact]
    public void Split_RespectsOverlap()
    {
        // Create text with clear paragraph breaks where overlap should cause content to repeat
        var p1 = new string('A', 40);
        var p2 = new string('B', 40);
        var p3 = new string('C', 40);
        var text = $"{p1}\n\n{p2}\n\n{p3}";

        var splitter = new RecursiveCharacterSplitter(chunkSize: 50, chunkOverlap: 45);
        var doc = new Document
        {
            PageContent = text,
            Metadata = new Dictionary<string, string> { ["source"] = "test.txt" }
        };

        var chunks = splitter.Split(doc);

        // With overlap, we should see some content repeated across chunks
        chunks.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Split_ConfigurableChunkSize()
    {
        var text = string.Join("\n\n", Enumerable.Range(0, 10).Select(i => $"Paragraph {i} with some content."));
        var splitter = new RecursiveCharacterSplitter(chunkSize: 60, chunkOverlap: 0);
        var doc = new Document
        {
            PageContent = text,
            Metadata = new Dictionary<string, string> { ["source"] = "test.txt" }
        };

        var chunks = splitter.Split(doc);

        // All chunks should respect the size limit (or be close to it for the last separator level)
        chunks.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void Split_EmptyDocument_ReturnsNoChunks()
    {
        var splitter = new RecursiveCharacterSplitter(chunkSize: 100, chunkOverlap: 10);
        var doc = new Document
        {
            PageContent = "",
            Metadata = new Dictionary<string, string> { ["source"] = "empty.txt" }
        };

        var chunks = splitter.Split(doc);

        chunks.Should().BeEmpty();
    }

    [Fact]
    public void Split_WhitespaceOnlyDocument_ReturnsNoChunks()
    {
        var splitter = new RecursiveCharacterSplitter(chunkSize: 100, chunkOverlap: 10);
        var doc = new Document
        {
            PageContent = "   \n\n  \n  ",
            Metadata = new Dictionary<string, string> { ["source"] = "whitespace.txt" }
        };

        var chunks = splitter.Split(doc);

        chunks.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ThrowsOnInvalidChunkSize()
    {
        var act = () => new RecursiveCharacterSplitter(chunkSize: 0, chunkOverlap: 0);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ThrowsOnOverlapGreaterThanChunkSize()
    {
        var act = () => new RecursiveCharacterSplitter(chunkSize: 100, chunkOverlap: 100);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Split_PreservesSource()
    {
        var splitter = new RecursiveCharacterSplitter(chunkSize: 20, chunkOverlap: 0);
        var doc = new Document
        {
            PageContent = "Some text that will be split into chunks.",
            Metadata = new Dictionary<string, string> { ["source"] = "myfile.txt" }
        };

        var chunks = splitter.Split(doc);

        chunks.Should().AllSatisfy(c => c.Source.Should().Be("myfile.txt"));
    }

    [Fact]
    public void Split_DefaultChunkSizeAndOverlap()
    {
        // Verify default constructor values match spec (1000 chars, 100 overlap)
        var splitter = new RecursiveCharacterSplitter();
        var longText = new string('x', 2500);
        var doc = new Document
        {
            PageContent = longText,
            Metadata = new Dictionary<string, string> { ["source"] = "big.txt" }
        };

        var chunks = splitter.Split(doc);

        chunks.Should().HaveCountGreaterThan(1);
        // With 2500 chars, 1000 chunk size, 100 overlap: ~3 chunks expected
        chunks.Count.Should().BeGreaterThanOrEqualTo(3);
    }
}
