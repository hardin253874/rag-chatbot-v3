# Sprint Contract B4 — Ingestion Endpoints + Query Rewrite

**Sprint:** B4
**Agent:** Backend Developer
**Date:** 2026-03-31
**Status:** Complete

---

## Scope

### 1. Ingestion Service (Core + Infrastructure)

**Interface:** `Core/Interfaces/IIngestionService.cs`
- `Task<string> IngestFileAsync(Stream fileStream, string originalFileName)`
- `Task<string> IngestUrlAsync(string url)`

**Implementation:** `Infrastructure/Ingestion/IngestionService.cs`
- Determines file type from extension (.md vs .txt)
- Saves uploaded file to temp path
- Loads document via TextFileLoader (for .md/.txt) or WebPageLoader (for URL)
- Splits using MarkdownSplitter for .md files, RecursiveCharacterSplitter for .txt/URL
- Generates document IDs via DocumentIdGenerator
- Calls PineconeService.StoreDocumentsAsync
- Cleans up temp files in finally block
- Returns success message with original filename or URL

### 2. Ingestion Controller

**File:** `Api/Controllers/IngestController.cs`
- `POST /ingest` — dual mode:
  - If request has `file` (multipart/form-data): file upload ingestion
  - If request has JSON body with `url`: URL ingestion
  - If neither: HTTP 400
- `GET /ingest/sources` — delegates to PineconeService.ListSourcesAsync
- `DELETE /ingest/reset` — delegates to PineconeService.ResetCollectionAsync

### 3. Query Rewrite Service (Core + Infrastructure)

**Interface:** `Core/Interfaces/IQueryRewriteService.cs`
- `Task<string> RewriteQueryAsync(string originalQuery)`

**Implementation:** `Infrastructure/QueryRewrite/QueryRewriteService.cs`
- Calls `{REWRITE_LLM_BASE_URL}/chat/completions`
- Headers: `Authorization: Bearer {key}`, `Content-Type: application/json`
- Body: `{ model, messages: [system, user], temperature: 0, max_tokens: 200 }`
- System prompt from spec section 5.2
- On ANY failure (config missing, API error, empty response): log warning, return original query

### 4. DI Registration
- `IIngestionService` -> `IngestionService`
- `IQueryRewriteService` -> `QueryRewriteService`
- Named HttpClient `"OpenAI"` for rewrite service

---

## Files to Create

| File | Purpose |
|------|---------|
| `Core/Interfaces/IIngestionService.cs` | Ingestion service interface |
| `Core/Interfaces/IQueryRewriteService.cs` | Query rewrite interface |
| `Infrastructure/Ingestion/IngestionService.cs` | Ingestion orchestration |
| `Infrastructure/QueryRewrite/QueryRewriteService.cs` | LLM query rewrite |
| `Api/Controllers/IngestController.cs` | Ingestion HTTP endpoints |
| `Tests/Ingestion/IngestionServiceTests.cs` | IngestionService unit tests |
| `Tests/QueryRewrite/QueryRewriteServiceTests.cs` | QueryRewriteService unit tests |
| `Tests/IngestControllerTests.cs` | IngestController integration tests |

## Files to Modify

| File | Change |
|------|--------|
| `Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs` | Register new services |

---

## Definition of Done

- [x] `POST /ingest` with `.md` file returns `{"success":true,"message":"Ingested file: <filename>"}` HTTP 200
- [x] `POST /ingest` with `.txt` file works identically
- [x] `POST /ingest` with JSON `{"url":"..."}` fetches URL, extracts text, ingests it, returns success
- [x] `POST /ingest` with no file and no URL returns HTTP 400 with error
- [x] `GET /ingest/sources` returns `{"success":true,"sources":[...]}`
- [x] `DELETE /ingest/reset` clears all records, returns `{"success":true,"message":"Knowledge base cleared."}`
- [x] Original filename stored as `source` (not temp path)
- [x] Temp files deleted after processing even on failure (try/finally)
- [x] Query rewrite calls `{REWRITE_LLM_BASE_URL}/chat/completions` with correct system prompt
- [x] If rewrite fails (missing config, API error, empty response), returns original query silently
- [x] `dotnet build` = 0 errors
- [x] `dotnet test` = all pass (96/96)

---

## Test Plan

### IngestionService Tests
1. IngestFile_MdFile_UsesMarkdownSplitter
2. IngestFile_TxtFile_UsesRecursiveCharacterSplitter
3. IngestFile_StoresOriginalFilename
4. IngestFile_CallsPineconeStoreDocuments
5. IngestFile_ReturnsSuccessMessage
6. IngestFile_CleansUpTempFile
7. IngestFile_CleansUpTempFileOnFailure
8. IngestUrl_LoadsAndSplitsAndStores
9. IngestUrl_ReturnsSuccessMessage

### QueryRewriteService Tests
1. RewriteQuery_CallsApiWithCorrectBody
2. RewriteQuery_ReturnsRewrittenQuery
3. RewriteQuery_FallsBackOnApiError
4. RewriteQuery_FallsBackOnEmptyResponse
5. RewriteQuery_FallsBackOnMissingConfig
6. RewriteQuery_UsesCorrectHeaders
7. RewriteQuery_UsesCorrectSystemPrompt

### IngestController Tests
1. PostIngest_WithFile_ReturnsSuccess
2. PostIngest_WithUrl_ReturnsSuccess
3. PostIngest_WithNoInput_Returns400
4. GetSources_ReturnsSources
5. DeleteReset_ReturnsSuccess
