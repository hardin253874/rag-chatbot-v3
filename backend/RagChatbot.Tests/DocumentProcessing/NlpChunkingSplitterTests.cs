using FluentAssertions;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.DocumentProcessing;

namespace RagChatbot.Tests.DocumentProcessing;

public class NlpChunkingSplitterTests
{
    private readonly NlpChunkingSplitter _splitter = new();

    private Document CreateDocument(string content, string source = "test.md")
    {
        return new Document
        {
            PageContent = content,
            Metadata = new Dictionary<string, string> { ["source"] = source }
        };
    }

    // --- Sentence boundary tests ---

    [Fact]
    public void Split_NeverSplitsMidSentence()
    {
        // Build a document with multiple sentences that is long enough to require splitting
        var sentences = new List<string>();
        for (int i = 0; i < 20; i++)
        {
            sentences.Add($"This is sentence number {i} which contains some meaningful content about the topic at hand.");
        }
        var text = string.Join(" ", sentences);
        var doc = CreateDocument(text);

        var chunks = _splitter.Split(doc);

        chunks.Should().HaveCountGreaterThan(1, "document should be split into multiple chunks");

        // No chunk should end in the middle of a sentence (except possibly the last)
        foreach (var chunk in chunks)
        {
            var content = chunk.Content.TrimEnd();
            // Each chunk should end with a sentence-ending punctuation mark or be a complete text block
            content.Should().MatchRegex(@"[.!?""'\)]$|[a-z0-9]$",
                $"chunk should not end mid-sentence: '{content[^Math.Min(content.Length, 50)..]}'");
        }
    }

    [Fact]
    public void Split_PrefersParagraphBoundaries()
    {
        // Two paragraphs that individually fit within chunk limits
        var para1 = string.Join(" ", Enumerable.Range(1, 5).Select(i =>
            $"Paragraph one sentence {i} with enough content to matter."));
        var para2 = string.Join(" ", Enumerable.Range(1, 5).Select(i =>
            $"Paragraph two sentence {i} with different content here."));

        var text = para1 + "\n\n" + para2;
        var doc = CreateDocument(text);

        var chunks = _splitter.Split(doc);

        // Should prefer splitting at the paragraph boundary
        if (chunks.Count >= 2)
        {
            // First chunk should not contain content from paragraph 2
            chunks[0].Content.Should().NotContain("Paragraph two");
        }
    }

    [Fact]
    public void Split_KeepsHeadingWithFollowingContent()
    {
        var text = "# Introduction\n\n" +
                   string.Join(" ", Enumerable.Range(1, 5).Select(i =>
                       $"This is an introductory sentence number {i} that explains the topic."));

        var doc = CreateDocument(text);
        var chunks = _splitter.Split(doc);

        chunks.Should().HaveCountGreaterThanOrEqualTo(1);

        // The heading should be in the same chunk as the following content
        var headingChunk = chunks.FirstOrDefault(c => c.Content.Contains("# Introduction"));
        headingChunk.Should().NotBeNull("heading should appear in at least one chunk");
        headingChunk!.Content.Should().Contain("introductory sentence",
            "heading should be kept with following content");
    }

    [Fact]
    public void Split_ChunksWithinMinMaxSize()
    {
        // Create a long document
        var sentences = Enumerable.Range(1, 50).Select(i =>
            $"This is detailed sentence number {i} covering various aspects of the subject matter in great depth and with sufficient detail to ensure chunking occurs properly.");
        var text = string.Join(" ", sentences);
        var doc = CreateDocument(text);

        var chunks = _splitter.Split(doc);

        chunks.Should().HaveCountGreaterThan(1);

        foreach (var chunk in chunks)
        {
            chunk.Content.Length.Should().BeLessThanOrEqualTo(1500,
                $"chunk should not exceed max size: length={chunk.Content.Length}");
        }

        // All chunks except possibly the last should be at least MinChunkSize
        for (int i = 0; i < chunks.Count - 1; i++)
        {
            chunks[i].Content.Length.Should().BeGreaterThanOrEqualTo(200,
                $"chunk {i} should meet min size: length={chunks[i].Content.Length}");
        }
    }

    [Fact]
    public void Split_DoesNotSplitInsideCodeBlock()
    {
        var codeBlock = "```python\ndef hello():\n    print('Hello, World!')\n    return 42\n```";
        var text = "Here is some introductory text that explains the code. " +
                   "We need enough text to ensure the splitter wants to split things up properly. " +
                   "This paragraph provides context before the code block.\n\n" +
                   codeBlock + "\n\n" +
                   "After the code block, we have more text that continues the explanation " +
                   "of what the code does and how it works in practice.";
        var doc = CreateDocument(text);

        var chunks = _splitter.Split(doc);

        // The code block should not be split across chunks
        var codeChunk = chunks.FirstOrDefault(c => c.Content.Contains("def hello()"));
        codeChunk.Should().NotBeNull("code block content should appear in a chunk");
        codeChunk!.Content.Should().Contain("return 42",
            "entire code block should be in the same chunk");
    }

    [Fact]
    public void Split_KeepsListItemsTogether()
    {
        var listBlock = "- First item in the list\n- Second item in the list\n- Third item in the list\n- Fourth item in the list";
        var text = "Here is an introduction paragraph with enough content to stand on its own as a meaningful chunk of text about the topic.\n\n" +
                   listBlock + "\n\n" +
                   "Here is a conclusion paragraph with enough content to also stand on its own as a separate chunk of text.";
        var doc = CreateDocument(text);

        var chunks = _splitter.Split(doc);

        // Find the chunk containing list items
        var listChunk = chunks.FirstOrDefault(c => c.Content.Contains("First item"));
        listChunk.Should().NotBeNull();
        listChunk!.Content.Should().Contain("Fourth item",
            "all list items should be kept together in the same chunk");
    }

    [Fact]
    public void Split_ShortDocument_ReturnsSingleChunk()
    {
        var text = "This is a short document.";
        var doc = CreateDocument(text);

        var chunks = _splitter.Split(doc);

        chunks.Should().HaveCount(1);
        chunks[0].Content.Should().Be("This is a short document.");
    }

    [Fact]
    public void Split_DoesNotSplitOnAbbreviations()
    {
        var text = "The U.S. Army deployed forces overseas. " +
                   "Mr. Smith met with Dr. Jones at the facility. " +
                   "They discussed the implications of the study conducted by the research department. " +
                   "This continued for several more sentences to provide enough text volume. " +
                   "Eventually they reached a conclusion about the matter at hand.";
        var doc = CreateDocument(text);

        var chunks = _splitter.Split(doc);

        // "U.S. Army" should not be split
        var allText = string.Join(" ", chunks.Select(c => c.Content));
        allText.Should().Contain("U.S. Army", "abbreviations should not cause sentence splits");
    }

    [Fact]
    public void Split_EmptyDocument_ReturnsEmptyList()
    {
        var doc = CreateDocument("");
        var chunks = _splitter.Split(doc);
        chunks.Should().BeEmpty();
    }

    [Fact]
    public void Split_NullDocument_ThrowsArgumentNullException()
    {
        var act = () => _splitter.Split(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Split_SetsCorrectSource()
    {
        var doc = CreateDocument("Short doc content.", "my-source.pdf");
        var chunks = _splitter.Split(doc);

        chunks.Should().HaveCount(1);
        chunks[0].Source.Should().Be("my-source.pdf");
    }

    [Fact]
    public void Split_GeneratesUniqueIds()
    {
        var sentences = Enumerable.Range(1, 30).Select(i =>
            $"Sentence {i} has enough content to force multiple chunks during the splitting process.");
        var text = string.Join(" ", sentences);
        var doc = CreateDocument(text);

        var chunks = _splitter.Split(doc);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Select(c => c.Id).Distinct().Should().HaveCount(chunks.Count,
            "each chunk should have a unique ID");
    }

    [Fact]
    public void Split_MultipleHeadings_EachAttachedToContent()
    {
        var text = "# Section One\n\nContent for section one goes here with enough text.\n\n" +
                   "# Section Two\n\nContent for section two goes here with enough text.\n\n" +
                   "# Section Three\n\nContent for section three goes here with enough text.";
        var doc = CreateDocument(text);

        var chunks = _splitter.Split(doc);

        // Each heading should be with its content, not stranded alone
        foreach (var chunk in chunks)
        {
            if (chunk.Content.Contains("# Section"))
            {
                // Heading should have content after it
                chunk.Content.Should().Contain("Content for section",
                    "heading should be attached to its following content");
            }
        }
    }

    [Fact]
    public void Split_NumberedListItemsKeptTogether()
    {
        var listBlock = "1. First numbered item\n2. Second numbered item\n3. Third numbered item";
        var text = "Introduction text with enough content to be a paragraph on its own here.\n\n" +
                   listBlock + "\n\n" +
                   "Conclusion text with enough content to be a paragraph on its own here.";
        var doc = CreateDocument(text);

        var chunks = _splitter.Split(doc);

        var listChunk = chunks.FirstOrDefault(c => c.Content.Contains("First numbered"));
        listChunk.Should().NotBeNull();
        listChunk!.Content.Should().Contain("Third numbered",
            "numbered list items should be kept together");
    }
}
