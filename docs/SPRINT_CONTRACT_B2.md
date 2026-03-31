# Sprint Contract B2 -- Document Processing

**Sprint:** B2
**Agent:** Backend Developer
**Date:** 2026-03-31
**Status:** Complete

---

## What Will Be Built

Document loading, text splitting, and chunk ID generation services for the ingestion pipeline. All interfaces in Core, implementations in Infrastructure.

### Models (Core)

- `Document` -- raw loaded content with metadata (source)
- `DocumentChunk` -- split chunk with ID, content, and source

### Interfaces (Core)

- `IDocumentLoader` -- loads a file from disk, returns a Document
- `IUrlLoader` -- fetches a URL, parses HTML, returns a Document
- `ITextSplitter` -- splits a Document into DocumentChunks

### Implementations (Infrastructure)

- `TextFileLoader` -- reads UTF-8 text files (.txt, .md), sets source to original filename
- `MarkdownSplitter` -- splits .md files by heading boundaries (# ## ### etc.), falls back to recursive splitting for oversized sections
- `RecursiveCharacterSplitter` -- splits by separators `\n\n`, `\n`, ` `, `` with configurable chunk size/overlap
- `WebPageLoader` -- fetches URL via HttpClient, strips HTML using HtmlAgilityPack, extracts visible text
- `DocumentIdGenerator` -- static helper: `doc_{UnixTimeMilliseconds}_{index}`

### DI Registration

- `InfrastructureServiceExtensions.AddInfrastructureServices()` in Infrastructure
- Wired up in Api's Program.cs

---

## Definition of Done

- [x] Text loader reads a `.txt` file -> document with content + source = original filename
- [x] Markdown loader reads `documents/test-sample.md` -> multiple chunks split at heading boundaries
- [x] Web page loader fetches a URL -> extracted visible text with source = URL
- [x] Recursive splitter splits text with configurable chunk size/overlap
- [x] Markdown-aware splitter used for `.md` files; recursive splitter for `.txt` and URL content
- [x] Document IDs follow `doc_{timestamp}_{index}` pattern, unique across batch
- [x] All services defined as interfaces in Core, implementations in Infrastructure
- [x] `dotnet build` = 0 errors
- [x] `dotnet test` = all pass (including new tests)

---

## Files to Be Created

Paths relative to `backend/`:

```
RagChatbot.Core/
  Models/
    Document.cs
    DocumentChunk.cs
  Interfaces/
    IDocumentLoader.cs
    IUrlLoader.cs
    ITextSplitter.cs

RagChatbot.Infrastructure/
  DocumentProcessing/
    TextFileLoader.cs
    MarkdownSplitter.cs
    RecursiveCharacterSplitter.cs
    WebPageLoader.cs
    DocumentIdGenerator.cs
  DependencyInjection/
    InfrastructureServiceExtensions.cs

RagChatbot.Tests/
  DocumentProcessing/
    TextFileLoaderTests.cs
    MarkdownSplitterTests.cs
    RecursiveCharacterSplitterTests.cs
    WebPageLoaderTests.cs
    DocumentIdGeneratorTests.cs
```

---

## Files to Be Modified

```
RagChatbot.Infrastructure/RagChatbot.Infrastructure.csproj  -- add HtmlAgilityPack
RagChatbot.Tests/RagChatbot.Tests.csproj                    -- add Infrastructure reference
RagChatbot.Api/Program.cs                                    -- wire up DI
```

---

## Technical Notes

- HtmlAgilityPack NuGet package for HTML parsing in WebPageLoader
- AppConfig already has ChunkSize (default 1000) and ChunkOverlap (default 100)
- Document ID format: `doc_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{index}`
- MarkdownSplitter heading regex: `^#{1,6}\s` (multiline)
- RecursiveCharacterSplitter separators: `\n\n`, `\n`, ` `, `` (in order)
- WebPageLoader strips `<script>`, `<style>`, `<nav>`, `<header>`, `<footer>` elements before extracting text
- Tests use real file I/O for TextFileLoader (temp files) and `documents/test-sample.md` for MarkdownSplitter

---

## Known Constraints

- Per ADR-009: Markdown-aware splitting for .md, recursive character for others
- Per ADR-010: Only MD, TXT, URL supported (no PDF/DOCX)
- ChunkSize and ChunkOverlap come from AppConfig (env vars)
- Source metadata uses original filename, never temp paths
