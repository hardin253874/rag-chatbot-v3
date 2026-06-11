using System.Text.Json;
using System.Text.Json.Serialization;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Infrastructure.DocumentProcessing;

/// <summary>
/// Question-based chunking. Stage 1: NLP structural pre-chunking. Stage 2: the LLM
/// converts each segment into self-contained Q&amp;A pairs. Each pair becomes one chunk.
/// Falls back to RecursiveCharacterSplitter if no pairs can be produced at all.
/// </summary>
public class QaChunkingSplitter : ITextSplitter
{
    private readonly NlpChunkingSplitter _nlpSplitter;
    private readonly ILlmService _llmService;
    private readonly RecursiveCharacterSplitter _fallbackSplitter;
    private Action<string>? _onProgress;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    private const string SystemPrompt = @"You are a knowledge-base builder for a retrieval system. You convert a passage of source text into a set of self-contained question-and-answer pairs that will be stored individually and retrieved out of context.

Follow these rules strictly:
1. GROUNDING: Use ONLY facts stated in the passage. Never add, infer, or invent information. If the passage contains no substantive facts (e.g. it is navigation, a menu, a footer, or pure boilerplate), return an empty array [].
2. QUESTIONS: Write natural questions a real user would type. Make each question standalone — it must name the specific subject, product, company, or entity, not rely on ""it"" or ""this"". Prefer the phrasing a curious outsider would use.
3. ANSWERS: Make every answer fully self-contained. State the subject by name inside the answer. The answer must make complete sense to someone who only sees this one pair and nothing else. Do not write ""as mentioned above"", ""the document says"", or any reference to surrounding text.
4. COVERAGE: Create one pair per distinct fact, capability, definition, step, or claim. Do not merge unrelated facts. Do not duplicate the same fact.
5. FIDELITY: Keep concrete details exactly as written — names, numbers, dates, prices, technologies, contact details, certifications.
6. LENGTH: Answers should be 1-4 sentences. Keep them tight.
7. OUTPUT: Return ONLY a JSON array of objects, each with exactly two string fields: ""question"" and ""answer"". No markdown, no code fences, no commentary.

Example output:
[{""question"":""What is Acme's flagship product?"",""answer"":""Acme's flagship product is the Acme Widget, a self-cleaning industrial sensor introduced in 2021.""}]";

    private const string UserPromptTemplate = @"Convert the following passage into self-contained Q&A pairs, following all rules. Return only the JSON array.

Passage:
---
{segment_text}
---";

    public QaChunkingSplitter(
        NlpChunkingSplitter nlpSplitter,
        ILlmService llmService,
        RecursiveCharacterSplitter fallbackSplitter)
    {
        _nlpSplitter = nlpSplitter ?? throw new ArgumentNullException(nameof(nlpSplitter));
        _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
        _fallbackSplitter = fallbackSplitter ?? throw new ArgumentNullException(nameof(fallbackSplitter));
    }

    /// <summary>
    /// Sets the progress callback for reporting status during splitting.
    /// Returns this instance for fluent usage.
    /// </summary>
    public QaChunkingSplitter WithProgress(Action<string>? onProgress)
    {
        _onProgress = onProgress;
        return this;
    }

    public List<DocumentChunk> Split(Document document)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        var source = document.Metadata.GetValueOrDefault("source", "unknown");

        // Stage 1: structural pre-chunking (reuse trusted NLP splitter)
        _onProgress?.Invoke("NLP pre-chunking...");
        var segments = _nlpSplitter.Split(document);
        if (segments.Count == 0)
            return new List<DocumentChunk>();
        _onProgress?.Invoke($"Pre-chunking produced {segments.Count} segments");

        // Stage 2: per-segment Q&A generation
        var pairs = new List<QaPair>();
        for (int i = 0; i < segments.Count; i++)
        {
            _onProgress?.Invoke($"Generating Q&A for segment {i + 1}/{segments.Count}...");
            try
            {
                pairs.AddRange(GenerateQaForSegment(segments[i].Content));
            }
            catch (Exception)
            {
                // Soft failure: skip this segment, keep going
                _onProgress?.Invoke($"Segment {i + 1} failed, skipping");
            }
        }

        // Drop pairs with a blank question or answer before assembling chunks
        var validPairs = pairs
            .Select(p => (Question: (p.Question ?? string.Empty).Trim(), Answer: (p.Answer ?? string.Empty).Trim()))
            .Where(p => p.Question.Length > 0 && p.Answer.Length > 0)
            .ToList();

        // Total failure → fall back so the document is still ingested
        if (validPairs.Count == 0)
        {
            _onProgress?.Invoke("No Q&A produced, falling back to recursive splitter");
            return _fallbackSplitter.Split(document);
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var chunks = new List<DocumentChunk>(validPairs.Count);
        for (int i = 0; i < validPairs.Count; i++)
        {
            chunks.Add(new DocumentChunk
            {
                Id = DocumentIdGenerator.Generate(i, timestamp),
                Content = $"Q: {validPairs[i].Question}\nA: {validPairs[i].Answer}",
                Source = source
            });
        }

        _onProgress?.Invoke($"Generated {chunks.Count} Q&A chunks");
        return chunks;
    }

    private List<QaPair> GenerateQaForSegment(string segmentText)
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = SystemPrompt },
            new() { Role = "user", Content = UserPromptTemplate.Replace("{segment_text}", segmentText) }
        };

        var response = Task.Run(() => _llmService.ChatWithToolsAsync(
            messages,
            new List<ToolDefinition>(),
            temperature: 0.0f)).GetAwaiter().GetResult();

        var json = ExtractJsonArray(response.Content);
        if (string.IsNullOrWhiteSpace(json))
            return new List<QaPair>();

        var parsed = JsonSerializer.Deserialize<List<QaPair>>(json, JsonOpts);
        return parsed ?? new List<QaPair>();
    }

    /// <summary>Strips ```json fences / surrounding prose so deserialize is robust.</summary>
    private static string ExtractJsonArray(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;
        var start = content.IndexOf('[');
        var end = content.LastIndexOf(']');
        return (start >= 0 && end > start) ? content.Substring(start, end - start + 1) : string.Empty;
    }

    private sealed class QaPair
    {
        [JsonPropertyName("question")] public string Question { get; set; } = string.Empty;
        [JsonPropertyName("answer")] public string Answer { get; set; } = string.Empty;
    }
}
