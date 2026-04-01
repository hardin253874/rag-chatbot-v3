using System.Text.Json;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Infrastructure.DocumentProcessing;

/// <summary>
/// Uses an LLM to split documents into semantically coherent chunks.
/// Falls back to RecursiveCharacterSplitter on any failure (invalid JSON, empty result, exception).
/// </summary>
public class SmartChunkingSplitter : ITextSplitter
{
    private readonly ILlmService _llmService;
    private readonly RecursiveCharacterSplitter _fallbackSplitter;

    private const string ChunkingPrompt = @"You are a document chunking assistant. Your task is to split the following document into semantically coherent chunks. Each chunk should:

1. Cover one coherent topic, concept, or logical section
2. Be self-contained — a reader should understand the chunk without needing the surrounding text
3. Preserve important context (keep headings with their content, keep lists together)
4. Be between 200 and 2000 characters (flexible — prioritize semantic coherence over size)

Rules:
- Do NOT split mid-sentence or mid-paragraph unless the paragraph is very long
- Do NOT add any text that is not in the original document
- Do NOT summarize or modify the content — return the exact original text
- If the document is short (under 500 characters), return it as a single chunk
- If a section has a heading, include the heading in that chunk

Return your response as a JSON array of strings, where each string is one chunk. Example:
[""chunk 1 text..."", ""chunk 2 text..."", ""chunk 3 text...""]

Document to chunk:
---
{document_text}
---";

    public SmartChunkingSplitter(ILlmService llmService, RecursiveCharacterSplitter fallbackSplitter)
    {
        _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
        _fallbackSplitter = fallbackSplitter ?? throw new ArgumentNullException(nameof(fallbackSplitter));
    }

    public List<DocumentChunk> Split(Document document)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        var source = document.Metadata.GetValueOrDefault("source", "unknown");

        try
        {
            var prompt = ChunkingPrompt.Replace("{document_text}", document.PageContent);

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
                return _fallbackSplitter.Split(document);
            }

            var parsed = JsonSerializer.Deserialize<List<string>>(response.Content);

            if (parsed == null || parsed.Count == 0)
            {
                return _fallbackSplitter.Split(document);
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            return parsed
                .Select((text, index) => new DocumentChunk
                {
                    Id = DocumentIdGenerator.Generate(index, timestamp),
                    Content = text,
                    Source = source
                })
                .ToList();
        }
        catch (JsonException)
        {
            return _fallbackSplitter.Split(document);
        }
        catch (Exception)
        {
            return _fallbackSplitter.Split(document);
        }
    }
}
