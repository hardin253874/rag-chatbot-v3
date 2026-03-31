using FluentAssertions;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.DocumentProcessing;

namespace RagChatbot.Tests.DocumentProcessing;

public class MarkdownSplitterTests
{
    [Fact]
    public void Split_SplitsByHeadings()
    {
        var splitter = new MarkdownSplitter(chunkSize: 5000, chunkOverlap: 0);
        var doc = new Document
        {
            PageContent = "# Heading 1\n\nContent one.\n\n## Heading 2\n\nContent two.",
            Metadata = new Dictionary<string, string> { ["source"] = "test.md" }
        };

        var chunks = splitter.Split(doc);

        chunks.Should().HaveCount(2);
        chunks[0].Content.Should().StartWith("# Heading 1");
        chunks[0].Content.Should().Contain("Content one.");
        chunks[1].Content.Should().StartWith("## Heading 2");
        chunks[1].Content.Should().Contain("Content two.");
    }

    [Fact]
    public void Split_PreservesSource()
    {
        var splitter = new MarkdownSplitter(chunkSize: 5000, chunkOverlap: 0);
        var doc = new Document
        {
            PageContent = "# Heading\n\nContent.",
            Metadata = new Dictionary<string, string> { ["source"] = "readme.md" }
        };

        var chunks = splitter.Split(doc);

        chunks.Should().AllSatisfy(c => c.Source.Should().Be("readme.md"));
    }

    [Fact]
    public void Split_GeneratesDocIds()
    {
        var splitter = new MarkdownSplitter(chunkSize: 5000, chunkOverlap: 0);
        var doc = new Document
        {
            PageContent = "# H1\n\nContent.\n\n## H2\n\nMore content.",
            Metadata = new Dictionary<string, string> { ["source"] = "test.md" }
        };

        var chunks = splitter.Split(doc);

        chunks.Should().AllSatisfy(c => c.Id.Should().MatchRegex(@"^doc_\d+_\d+$"));
        chunks.Select(c => c.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Split_PreambleBeforeFirstHeading_BecomesChunk()
    {
        var splitter = new MarkdownSplitter(chunkSize: 5000, chunkOverlap: 0);
        var doc = new Document
        {
            PageContent = "This is preamble text.\n\n# Heading 1\n\nContent.",
            Metadata = new Dictionary<string, string> { ["source"] = "test.md" }
        };

        var chunks = splitter.Split(doc);

        chunks.Should().HaveCount(2);
        chunks[0].Content.Should().Be("This is preamble text.");
        chunks[1].Content.Should().StartWith("# Heading 1");
    }

    [Fact]
    public void Split_OversizedSection_FallsBackToRecursiveSplitting()
    {
        // Create a section that exceeds chunk size
        var longContent = new string('x', 200);
        var splitter = new MarkdownSplitter(chunkSize: 50, chunkOverlap: 10);
        var doc = new Document
        {
            PageContent = $"# Heading\n\n{longContent}",
            Metadata = new Dictionary<string, string> { ["source"] = "test.md" }
        };

        var chunks = splitter.Split(doc);

        // The oversized section should be split into multiple chunks
        chunks.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public void Split_NoHeadings_ReturnsWholeDocumentAsOneChunk()
    {
        var splitter = new MarkdownSplitter(chunkSize: 5000, chunkOverlap: 0);
        var doc = new Document
        {
            PageContent = "Just plain text without any headings.\nAnother line.",
            Metadata = new Dictionary<string, string> { ["source"] = "test.md" }
        };

        var chunks = splitter.Split(doc);

        chunks.Should().HaveCount(1);
        chunks[0].Content.Should().Contain("Just plain text");
    }

    [Fact]
    public void Split_MultipleHeadingLevels()
    {
        var splitter = new MarkdownSplitter(chunkSize: 5000, chunkOverlap: 0);
        var doc = new Document
        {
            PageContent = "# H1\n\nContent 1.\n\n## H2\n\nContent 2.\n\n### H3\n\nContent 3.",
            Metadata = new Dictionary<string, string> { ["source"] = "test.md" }
        };

        var chunks = splitter.Split(doc);

        chunks.Should().HaveCount(3);
    }

    [Fact]
    public void Split_EmptyDocument_ReturnsNoChunks()
    {
        var splitter = new MarkdownSplitter(chunkSize: 5000, chunkOverlap: 0);
        var doc = new Document
        {
            PageContent = "",
            Metadata = new Dictionary<string, string> { ["source"] = "empty.md" }
        };

        var chunks = splitter.Split(doc);

        chunks.Should().BeEmpty();
    }

    [Fact]
    public void Split_TestSampleMd_ProducesMultipleChunks()
    {
        // Test with the real test-sample.md file
        var testSamplePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "documents", "test-sample.md"));

        if (!File.Exists(testSamplePath))
        {
            // Skip if file not found (CI environment)
            return;
        }

        var content = File.ReadAllText(testSamplePath);
        var splitter = new MarkdownSplitter(chunkSize: 1000, chunkOverlap: 100);
        var doc = new Document
        {
            PageContent = content,
            Metadata = new Dictionary<string, string> { ["source"] = "test-sample.md" }
        };

        var chunks = splitter.Split(doc);

        chunks.Should().HaveCountGreaterThan(1, "test-sample.md has multiple headings");
        chunks.Should().AllSatisfy(c =>
        {
            c.Source.Should().Be("test-sample.md");
            c.Id.Should().MatchRegex(@"^doc_\d+_\d+$");
            c.Content.Should().NotBeNullOrWhiteSpace();
        });
    }
}
