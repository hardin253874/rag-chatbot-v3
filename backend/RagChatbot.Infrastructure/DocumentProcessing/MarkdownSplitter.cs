using System.Text.RegularExpressions;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Infrastructure.DocumentProcessing;

/// <summary>
/// Splits markdown documents by heading boundaries (# ## ### etc.).
/// Sections that exceed the configured chunk size are further split using recursive character splitting.
/// </summary>
public partial class MarkdownSplitter : ITextSplitter
{
    private readonly int _chunkSize;
    private readonly int _chunkOverlap;

    [GeneratedRegex(@"^(#{1,6}\s)", RegexOptions.Multiline)]
    private static partial Regex HeadingRegex();

    public MarkdownSplitter(int chunkSize = 1000, int chunkOverlap = 100)
    {
        _chunkSize = chunkSize;
        _chunkOverlap = chunkOverlap;
    }

    public List<DocumentChunk> Split(Document document)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        var source = document.Metadata.GetValueOrDefault("source", "unknown");
        var sections = SplitByHeadings(document.PageContent);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var chunks = new List<DocumentChunk>();
        var index = 0;

        var recursiveSplitter = new RecursiveCharacterSplitter(_chunkSize, _chunkOverlap);

        foreach (var section in sections)
        {
            var trimmed = section.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            if (trimmed.Length <= _chunkSize)
            {
                chunks.Add(new DocumentChunk
                {
                    Id = DocumentIdGenerator.Generate(index++, timestamp),
                    Content = trimmed,
                    Source = source
                });
            }
            else
            {
                // Section exceeds chunk size, fall back to recursive splitting
                var subDoc = new Document
                {
                    PageContent = trimmed,
                    Metadata = document.Metadata
                };
                var subTexts = recursiveSplitter.SplitText(trimmed);
                foreach (var text in subTexts)
                {
                    chunks.Add(new DocumentChunk
                    {
                        Id = DocumentIdGenerator.Generate(index++, timestamp),
                        Content = text,
                        Source = source
                    });
                }
            }
        }

        return chunks;
    }

    /// <summary>
    /// Splits markdown text into sections at heading boundaries.
    /// Each section starts with its heading line.
    /// </summary>
    internal static List<string> SplitByHeadings(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new List<string>();

        var sections = new List<string>();
        var matches = HeadingRegex().Matches(text);

        if (matches.Count == 0)
        {
            // No headings found, return entire text as one section
            sections.Add(text);
            return sections;
        }

        // Content before first heading (if any)
        var firstHeadingIndex = matches[0].Index;
        if (firstHeadingIndex > 0)
        {
            var preamble = text[..firstHeadingIndex].Trim();
            if (!string.IsNullOrEmpty(preamble))
                sections.Add(preamble);
        }

        // Each heading starts a new section
        for (int i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var section = text[start..end].Trim();
            if (!string.IsNullOrEmpty(section))
                sections.Add(section);
        }

        return sections;
    }
}
