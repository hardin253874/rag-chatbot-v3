using System.Text;
using System.Text.RegularExpressions;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Infrastructure.DocumentProcessing;

/// <summary>
/// Splits documents by linguistic boundaries (sentences, paragraphs, headings)
/// without any LLM calls. Respects code blocks, bullet lists, and abbreviations.
/// </summary>
public class NlpChunkingSplitter : ITextSplitter
{
    private const int MinChunkSize = 200;
    private const int MaxChunkSize = 1500;

    // Sentence boundary: split after .!? followed by whitespace and uppercase letter (or quote/paren)
    private static readonly Regex SentenceBoundaryRegex = new(
        @"(?<=[.!?])\s+(?=[A-Z""'\(])",
        RegexOptions.Compiled);

    // Abbreviation pattern: single uppercase letter followed by period (e.g., U. S. Mr.)
    private static readonly Regex AbbreviationPattern = new(
        @"\b[A-Z]\.\s*$",
        RegexOptions.Compiled);

    // List item pattern
    private static readonly Regex ListItemPattern = new(
        @"^(\s*[-*]|\s*\d+\.)\s",
        RegexOptions.Compiled);

    public List<DocumentChunk> Split(Document document)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        var source = document.Metadata.GetValueOrDefault("source", "unknown");
        var text = document.PageContent;

        // Short document: return as single chunk
        if (string.IsNullOrWhiteSpace(text) || text.Length < MinChunkSize)
        {
            var trimmed = text?.Trim();
            if (string.IsNullOrEmpty(trimmed))
                return new List<DocumentChunk>();

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return new List<DocumentChunk>
            {
                new()
                {
                    Id = DocumentIdGenerator.Generate(0, timestamp),
                    Content = trimmed,
                    Source = source
                }
            };
        }

        var segments = SplitIntoSegments(text);
        var chunks = MergeSegmentsIntoChunks(segments);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        return chunks
            .Select((content, index) => new DocumentChunk
            {
                Id = DocumentIdGenerator.Generate(index, ts),
                Content = content,
                Source = source
            })
            .ToList();
    }

    /// <summary>
    /// Splits text into logical segments: paragraphs, then sentences within paragraphs.
    /// Respects code blocks, headings, and list items.
    /// </summary>
    private List<Segment> SplitIntoSegments(string text)
    {
        var segments = new List<Segment>();
        var paragraphs = SplitIntoParagraphs(text);

        foreach (var para in paragraphs)
        {
            if (string.IsNullOrWhiteSpace(para))
                continue;

            // Check if this paragraph is a fenced code block
            if (IsCodeBlock(para))
            {
                segments.Add(new Segment(para.Trim(), SegmentType.CodeBlock));
                continue;
            }

            // Check if this paragraph is a heading
            if (IsHeading(para))
            {
                segments.Add(new Segment(para.Trim(), SegmentType.Heading));
                continue;
            }

            // Check if this paragraph is a list block
            if (IsListBlock(para))
            {
                segments.Add(new Segment(para.Trim(), SegmentType.ListBlock));
                continue;
            }

            // Split paragraph into sentences
            var sentences = SplitIntoSentences(para.Trim());
            foreach (var sentence in sentences)
            {
                if (!string.IsNullOrWhiteSpace(sentence))
                {
                    segments.Add(new Segment(sentence.Trim(), SegmentType.Sentence));
                }
            }

            // Mark paragraph boundary
            segments.Add(new Segment(string.Empty, SegmentType.ParagraphBreak));
        }

        // Remove trailing paragraph break
        if (segments.Count > 0 && segments[^1].Type == SegmentType.ParagraphBreak)
        {
            segments.RemoveAt(segments.Count - 1);
        }

        return segments;
    }

    /// <summary>
    /// Splits text into paragraphs, keeping fenced code blocks intact.
    /// </summary>
    private List<string> SplitIntoParagraphs(string text)
    {
        var paragraphs = new List<string>();
        var lines = text.Split('\n');
        var currentBlock = new StringBuilder();
        var insideCodeBlock = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmedLine = line.TrimStart();

            // Track code block state
            if (trimmedLine.StartsWith("```"))
            {
                if (!insideCodeBlock)
                {
                    // Starting a code block -- flush current paragraph first
                    if (currentBlock.Length > 0)
                    {
                        paragraphs.Add(currentBlock.ToString());
                        currentBlock.Clear();
                    }
                    insideCodeBlock = true;
                    currentBlock.AppendLine(line);
                    continue;
                }
                else
                {
                    // Ending a code block
                    currentBlock.AppendLine(line);
                    insideCodeBlock = false;
                    paragraphs.Add(currentBlock.ToString());
                    currentBlock.Clear();
                    continue;
                }
            }

            if (insideCodeBlock)
            {
                currentBlock.AppendLine(line);
                continue;
            }

            // Empty line = paragraph break (outside code blocks)
            if (string.IsNullOrWhiteSpace(line))
            {
                if (currentBlock.Length > 0)
                {
                    paragraphs.Add(currentBlock.ToString());
                    currentBlock.Clear();
                }
                continue;
            }

            if (currentBlock.Length > 0)
                currentBlock.AppendLine();
            currentBlock.Append(line);
        }

        // Flush remaining content
        if (currentBlock.Length > 0)
        {
            paragraphs.Add(currentBlock.ToString());
        }

        return paragraphs;
    }

    /// <summary>
    /// Splits a paragraph into sentences using regex, respecting abbreviations.
    /// </summary>
    private List<string> SplitIntoSentences(string paragraph)
    {
        var sentences = new List<string>();
        var parts = SentenceBoundaryRegex.Split(paragraph);

        var current = new StringBuilder();

        foreach (var part in parts)
        {
            if (current.Length > 0)
            {
                // Check if the previous segment ends with an abbreviation pattern
                var currentText = current.ToString();
                if (AbbreviationPattern.IsMatch(currentText))
                {
                    // Don't split -- it's an abbreviation
                    current.Append(' ');
                    current.Append(part);
                    continue;
                }

                // Commit the previous sentence
                sentences.Add(currentText.Trim());
                current.Clear();
            }

            current.Append(part);
        }

        if (current.Length > 0)
        {
            sentences.Add(current.ToString().Trim());
        }

        return sentences;
    }

    /// <summary>
    /// Merges segments into chunks respecting min/max size constraints.
    /// Prefers splitting at paragraph boundaries.
    /// </summary>
    private List<string> MergeSegmentsIntoChunks(List<Segment> segments)
    {
        var chunks = new List<string>();
        var currentChunk = new StringBuilder();
        var pendingHeading = (string?)null;

        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];

            // Skip paragraph breaks but use them as preferred split points
            if (segment.Type == SegmentType.ParagraphBreak)
            {
                // Check if current chunk is at or above minimum size -- good split point
                if (currentChunk.Length >= MinChunkSize)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                }
                else if (currentChunk.Length > 0)
                {
                    // Add paragraph separator within the chunk
                    currentChunk.AppendLine();
                    currentChunk.AppendLine();
                }
                continue;
            }

            // Handle headings: attach to following content
            if (segment.Type == SegmentType.Heading)
            {
                // If current chunk is big enough, flush it
                if (currentChunk.Length >= MinChunkSize)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                }

                pendingHeading = segment.Text;
                continue;
            }

            var textToAdd = segment.Text;

            // Prepend pending heading
            if (pendingHeading != null)
            {
                textToAdd = pendingHeading + "\n\n" + textToAdd;
                pendingHeading = null;
            }

            // Check if adding this segment would exceed max
            var projectedLength = currentChunk.Length + (currentChunk.Length > 0 ? 2 : 0) + textToAdd.Length;

            if (projectedLength > MaxChunkSize)
            {
                // Flush current chunk if it has content
                if (currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                }

                // If the segment itself is larger than max, add it as its own chunk
                if (textToAdd.Length > MaxChunkSize)
                {
                    chunks.Add(textToAdd.Trim());
                    continue;
                }
            }

            if (currentChunk.Length > 0)
            {
                // Use appropriate separator
                if (segment.Type == SegmentType.CodeBlock || segment.Type == SegmentType.ListBlock)
                {
                    currentChunk.AppendLine();
                    currentChunk.AppendLine();
                }
                else
                {
                    currentChunk.Append(' ');
                }
            }

            currentChunk.Append(textToAdd);
        }

        // Handle any remaining pending heading
        if (pendingHeading != null)
        {
            if (currentChunk.Length > 0)
            {
                currentChunk.AppendLine();
                currentChunk.AppendLine();
            }
            currentChunk.Append(pendingHeading);
        }

        // Flush remaining content
        if (currentChunk.Length > 0)
        {
            var remaining = currentChunk.ToString().Trim();
            if (remaining.Length > 0)
            {
                // If the remaining chunk is too small and we have previous chunks, merge with last
                if (remaining.Length < MinChunkSize && chunks.Count > 0 &&
                    chunks[^1].Length + remaining.Length + 2 <= MaxChunkSize)
                {
                    chunks[^1] = chunks[^1] + "\n\n" + remaining;
                }
                else
                {
                    chunks.Add(remaining);
                }
            }
        }

        return chunks;
    }

    private static bool IsCodeBlock(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.StartsWith("```");
    }

    private static bool IsHeading(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.StartsWith('#');
    }

    private static bool IsListBlock(string text)
    {
        var lines = text.Split('\n');
        // A list block is when most lines start with list item markers
        int listLineCount = 0;
        int totalNonEmpty = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            totalNonEmpty++;
            if (ListItemPattern.IsMatch(line))
                listLineCount++;
        }

        return totalNonEmpty > 0 && listLineCount >= (totalNonEmpty + 1) / 2;
    }

    private enum SegmentType
    {
        Sentence,
        Heading,
        CodeBlock,
        ListBlock,
        ParagraphBreak
    }

    private record Segment(string Text, SegmentType Type);
}
