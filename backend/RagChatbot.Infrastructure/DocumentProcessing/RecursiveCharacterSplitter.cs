using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Infrastructure.DocumentProcessing;

/// <summary>
/// Splits text using recursive character splitting with configurable chunk size and overlap.
/// Separators tried in order: "\n\n", "\n", " ", "" (character-level).
/// </summary>
public class RecursiveCharacterSplitter : ITextSplitter
{
    private readonly int _chunkSize;
    private readonly int _chunkOverlap;
    private static readonly string[] Separators = ["\n\n", "\n", " ", ""];

    public RecursiveCharacterSplitter(int chunkSize = 1000, int chunkOverlap = 100)
    {
        if (chunkSize <= 0)
            throw new ArgumentException("Chunk size must be positive.", nameof(chunkSize));
        if (chunkOverlap < 0)
            throw new ArgumentException("Chunk overlap must be non-negative.", nameof(chunkOverlap));
        if (chunkOverlap >= chunkSize)
            throw new ArgumentException("Chunk overlap must be less than chunk size.", nameof(chunkOverlap));

        _chunkSize = chunkSize;
        _chunkOverlap = chunkOverlap;
    }

    public List<DocumentChunk> Split(Document document)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        var source = document.Metadata.GetValueOrDefault("source", "unknown");
        var texts = SplitText(document.PageContent, Separators);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        return texts
            .Select((text, index) => new DocumentChunk
            {
                Id = DocumentIdGenerator.Generate(index, timestamp),
                Content = text,
                Source = source
            })
            .ToList();
    }

    /// <summary>
    /// Splits text into chunks, exposed for reuse by MarkdownSplitter.
    /// </summary>
    public List<string> SplitText(string text)
    {
        return SplitText(text, Separators);
    }

    private List<string> SplitText(string text, string[] separators)
    {
        var results = new List<string>();

        if (string.IsNullOrEmpty(text))
            return results;

        if (text.Length <= _chunkSize)
        {
            var trimmed = text.Trim();
            if (trimmed.Length > 0)
                results.Add(trimmed);
            return results;
        }

        // Find the appropriate separator
        var separator = separators.Length > 0 ? separators[^1] : "";
        var remainingSeparators = separators;

        for (int i = 0; i < separators.Length; i++)
        {
            if (string.IsNullOrEmpty(separators[i]))
            {
                separator = separators[i];
                remainingSeparators = separators[i..];
                break;
            }
            if (text.Contains(separators[i]))
            {
                separator = separators[i];
                remainingSeparators = separators[(i + 1)..];
                break;
            }
        }

        // Split by the chosen separator
        string[] splits;
        if (string.IsNullOrEmpty(separator))
        {
            // Character-level split
            splits = text.Select(c => c.ToString()).ToArray();
        }
        else
        {
            splits = text.Split(separator);
        }

        // Merge splits into chunks
        var currentChunk = new List<string>();
        var currentLength = 0;

        foreach (var split in splits)
        {
            var splitLength = split.Length;
            var separatorLength = separator.Length;
            var projectedLength = currentLength + splitLength + (currentChunk.Count > 0 ? separatorLength : 0);

            if (projectedLength > _chunkSize && currentChunk.Count > 0)
            {
                // Flush current chunk
                var merged = string.Join(separator, currentChunk).Trim();
                if (merged.Length > 0)
                {
                    if (merged.Length > _chunkSize && remainingSeparators.Length > 0)
                    {
                        // Recursively split oversized chunks with next separator
                        results.AddRange(SplitText(merged, remainingSeparators));
                    }
                    else
                    {
                        results.Add(merged);
                    }
                }

                // Start new chunk with overlap from end of previous
                currentChunk = GetOverlapSplits(currentChunk, separator);
                currentLength = currentChunk.Count > 0
                    ? string.Join(separator, currentChunk).Length
                    : 0;
            }

            currentChunk.Add(split);
            currentLength = string.Join(separator, currentChunk).Length;
        }

        // Flush remaining
        if (currentChunk.Count > 0)
        {
            var merged = string.Join(separator, currentChunk).Trim();
            if (merged.Length > 0)
            {
                if (merged.Length > _chunkSize && remainingSeparators.Length > 0)
                {
                    results.AddRange(SplitText(merged, remainingSeparators));
                }
                else
                {
                    results.Add(merged);
                }
            }
        }

        return results;
    }

    private List<string> GetOverlapSplits(List<string> splits, string separator)
    {
        if (_chunkOverlap == 0)
            return new List<string>();

        var result = new List<string>();
        var totalLength = 0;

        // Walk backwards through splits to build overlap
        for (int i = splits.Count - 1; i >= 0; i--)
        {
            var addLength = splits[i].Length + (result.Count > 0 ? separator.Length : 0);
            if (totalLength + addLength > _chunkOverlap && result.Count > 0)
                break;

            result.Insert(0, splits[i]);
            totalLength += addLength;
        }

        return result;
    }
}
