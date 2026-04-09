using System.Text;
using System.Text.Json;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Infrastructure.DocumentProcessing;

/// <summary>
/// Two-stage hybrid chunking: NLP structural pre-chunking (stage 1) followed by
/// LLM batch refinement (stage 2). Falls back to NLP segments on any LLM failure.
/// </summary>
public class HybridChunkingSplitter : ITextSplitter
{
    private readonly NlpChunkingSplitter _nlpSplitter;
    private readonly ILlmService _llmService;
    private readonly RecursiveCharacterSplitter _fallbackSplitter;

    private Action<string>? _onProgress;

    private const string RefinementPrompt = @"You are a document chunking assistant. Below are pre-split segments of a document, separated by structural boundaries (paragraphs, headings, sentences). Your task is to review these segments and produce the final list of semantically coherent chunks.

For each segment, decide:
1. KEEP — the segment covers one coherent topic, keep as-is
2. SPLIT — the segment contains multiple distinct topics, split it
3. MERGE — the segment is too short or incomplete, merge with an adjacent segment

Rules:
- Each final chunk should cover one coherent topic or concept
- Each chunk should be self-contained — readable without surrounding context
- Keep headings attached to their content
- Target chunk size: 200-2000 characters (flexible — prioritize coherence over size)
- Do NOT add, summarize, or modify any text — return the exact original text
- Do NOT split mid-sentence

Pre-split segments:
---
{segments}
---

Return your response as a JSON array of strings, where each string is one final chunk.
Example: [""chunk 1 text..."", ""chunk 2 text..."", ""chunk 3 text...""]";

    public HybridChunkingSplitter(
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
    public HybridChunkingSplitter WithProgress(Action<string>? onProgress)
    {
        _onProgress = onProgress;
        return this;
    }

    public List<DocumentChunk> Split(Document document)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        var source = document.Metadata.GetValueOrDefault("source", "unknown");

        // Stage 1: NLP pre-chunking
        _onProgress?.Invoke("NLP pre-chunking...");
        var nlpChunks = _nlpSplitter.Split(document);
        _onProgress?.Invoke($"NLP pre-chunking produced {nlpChunks.Count} segments");

        if (nlpChunks.Count == 0)
            return nlpChunks;

        try
        {
            // Stage 2: LLM batch refinement
            _onProgress?.Invoke("LLM refining segments...");

            var segmentsText = FormatSegments(nlpChunks);
            var prompt = RefinementPrompt.Replace("{segments}", segmentsText);

            var messages = new List<ChatMessage>
            {
                new() { Role = "user", Content = prompt }
            };

            var response = Task.Run(() => _llmService.ChatWithToolsAsync(
                messages,
                new List<ToolDefinition>(),
                temperature: 0.0f)).GetAwaiter().GetResult();

            if (string.IsNullOrWhiteSpace(response.Content))
            {
                _onProgress?.Invoke("LLM returned empty response, using NLP segments");
                return nlpChunks;
            }

            var parsed = JsonSerializer.Deserialize<List<string>>(response.Content);

            if (parsed == null || parsed.Count == 0)
            {
                _onProgress?.Invoke("LLM returned empty array, using NLP segments");
                return nlpChunks;
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var refinedChunks = parsed
                .Select((text, index) => new DocumentChunk
                {
                    Id = DocumentIdGenerator.Generate(index, timestamp),
                    Content = text,
                    Source = source
                })
                .ToList();

            _onProgress?.Invoke($"Refined into {refinedChunks.Count} chunks");
            return refinedChunks;
        }
        catch (JsonException)
        {
            _onProgress?.Invoke("LLM returned invalid JSON, using NLP segments");
            return nlpChunks;
        }
        catch (Exception)
        {
            _onProgress?.Invoke("LLM call failed, using NLP segments");
            return nlpChunks;
        }
    }

    private static string FormatSegments(List<DocumentChunk> chunks)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < chunks.Count; i++)
        {
            sb.AppendLine($"[{i + 1}]");
            sb.AppendLine(chunks[i].Content);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
