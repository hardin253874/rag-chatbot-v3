# Sprint Evaluation Re-Run

**Date:** 2026-03-31
**Evaluator:** QA Specialist Agent
**Servers:** Backend http://localhost:3010, Frontend http://localhost:3000
**Context:** Re-evaluation after 5 fixes applied from initial evaluation

---

## Previous Issues Status

| # | Issue | Status | Evidence |
|---|-------|--------|----------|
| 1 | Pinecone index missing integrated inference config | FIXED | Index describes `embed.model: "llama-text-embed-v2"`, `field_map.text: "chunk_text"`, status Ready |
| 2 | `useConfig.ts` missing fallback URL | FIXED | Line 25: `process.env.NEXT_PUBLIC_API_URL \|\| "http://localhost:3010"` |
| 3 | `.env.local.example` wrong port (5000) | FIXED | Now reads `http://localhost:3010` |
| 4 | `ChatController` no try-catch, empty 500 on error | FIXED | Lines 41-62: try-catch streams error as SSE chunk + done event |
| 5 | `PineconeService.ResetCollectionAsync` fails on 404 | FIXED | Line 142: `response.StatusCode != HttpStatusCode.NotFound` |

---

## Backend HTTP Tests

| # | Test | Result | Detail |
|---|------|--------|--------|
| 1 | `GET /health` | PASS | Returns `{"status":"ok"}` |
| 2 | `GET /config` | PASS | Returns `{"rewriteLlm":{"baseUrl":"...","model":"gpt-4o-mini"}}` - no API keys exposed |
| 3 | `POST /ingest` (file upload) | **FAIL** | 500 error: Pinecone upsert returns 400 "Missing or invalid field: _id" |
| 4 | `GET /ingest/sources` | PASS | Returns `{"success":true,"sources":[]}` (empty after reset) |
| 5 | `POST /chat` (valid question) | PASS | SSE stream: chunk event with text + done event |
| 6 | `POST /chat` (empty question) | PASS | Returns 400 `{"error":"Missing question"}` |
| 7 | `DELETE /ingest/reset` | PASS | Returns `{"success":true,"message":"Knowledge base cleared."}` |

### NEW P0 Issue: Pinecone Upsert Uses Wrong Request Format

**File:** `backend/RagChatbot.Infrastructure/VectorStore/PineconeService.cs`, `StoreDocumentsAsync` method

**Root Cause:** The Pinecone Records API (`/records/namespaces/{ns}/upsert`) expects **NDJSON** (newline-delimited JSON) with `Content-Type: application/x-ndjson`, where each line is a single record object:

```
{"_id":"doc_1_0","chunk_text":"Hello world","source":"test.md"}
{"_id":"doc_1_1","chunk_text":"Second chunk","source":"test.md"}
```

The current code sends a **JSON object wrapping an array** with `Content-Type: application/json`:

```json
{"records":[{"_id":"doc_1_0","chunk_text":"Hello world","source":"test.md"}]}
```

**Verified by direct curl:**
- `application/json` with `{"records":[...]}` -> 400 "Missing or invalid field: _id"
- `application/x-ndjson` with one record per line -> 200 success, record searchable

**Impact:** All document ingestion is broken. No documents can be stored in Pinecone. The chat endpoint works but always returns "no relevant information" since the KB is always empty.

**Fix Required in `StoreDocumentsAsync`:**
1. Change Content-Type from `application/json` to `application/x-ndjson`
2. Serialize each record as a separate JSON line (no wrapping object)
3. Use `--data-binary` equivalent (preserve newlines)

**Unit Tests Also Wrong:** `PineconeServiceTests.StoreDocumentsAsync_SendsCorrectJsonBody` (line 103) validates the incorrect `{"records":[...]}` format. Tests must be updated to validate NDJSON format.

---

## Frontend Tests (Playwright)

| # | Test | Result | Detail |
|---|------|--------|--------|
| 1 | Page loads, header shows "RAG Chatbot" | PASS | Header: "RAG Chatbot v3 Connected" |
| 2 | Status indicator "Connected" (green) | PASS | Green dot + "Connected" text visible top-right |
| 3 | Settings with LLM model info | PASS | Shows Model: gpt-4o-mini, Base URL: https://api.openai.com/v1 |
| 4 | KB panel (URL, upload, activity, list/clear) | PASS | All elements present: Add URL, Upload File, Activity log, List Resources, Clear Knowledge Base |
| 5 | Chat area with empty state | PASS | Shows "Ask a question about your documents" empty state |
| 6 | Type question, streaming response | PASS | Response appears: "I couldn't find any relevant information in the knowledge base." |
| 7 | Source citations | PASS | Source citation area present (no sources shown since KB is empty - expected) |

**Screenshots saved to:** `playwright_screenshots/rerun-01-page-load.png` through `rerun-05-final.png`

---

## Code Quality

| Check | Result | Detail |
|-------|--------|--------|
| `dotnet build` | PASS | 0 errors, 0 warnings |
| `dotnet test` | PASS | 137 passed, 0 failed, 0 skipped (324ms) |
| `npx tsc --noEmit` | PASS | 0 errors |
| `npm run lint` | PASS | No ESLint warnings or errors |

**Note:** Unit tests pass but `PineconeServiceTests` validate an incorrect request format (JSON array wrapper instead of NDJSON). The tests are internally consistent but do not match the real Pinecone API contract. This means the tests provide false confidence.

---

## Spec Fidelity

| Spec Requirement | Status | Detail |
|-----------------|--------|--------|
| POST /ingest ingests file | FAIL | Pinecone upsert format is wrong, ingestion always fails |
| POST /chat with SSE streaming | PASS | chunk/done events stream correctly |
| GET /ingest/sources lists sources | PASS | Returns correct format, but always empty due to ingest bug |
| DELETE /ingest/reset clears KB | PASS | Succeeds, 404 handled gracefully |
| GET /config returns config safely | PASS | Returns rewriteLlm config, no secrets |
| GET /health returns ok | PASS | Returns `{"status":"ok"}` |
| Pinecone integrated inference | WARN | Index configured correctly, but upsert code uses wrong format |
| Frontend connected status | PASS | Previously failing, now fixed |
| Error handling in chat | PASS | Try-catch streams error + done via SSE |

---

## Findings Summary

### Resolved from Previous Evaluation (5/5)

All five previously identified issues have been properly fixed.

### New Issue Found (1)

| Severity | Issue | Location |
|----------|-------|----------|
| **P0** | Pinecone upsert uses JSON array format instead of NDJSON | `PineconeService.StoreDocumentsAsync` + unit tests |

---

## Verdicts

| Category | Verdict | Rationale |
|----------|---------|-----------|
| **Correctness** | **FAIL** | Document ingestion is broken due to wrong Pinecone API request format. Core RAG pipeline cannot function without working ingestion. |
| **Code Quality** | **PASS** | Build clean, 137 tests pass, TypeScript clean, no lint errors. However, note that unit tests validate the wrong API contract. |
| **Spec Fidelity** | **WARN** | 5/6 endpoints work correctly. POST /ingest fails against real Pinecone. The spec requires Pinecone integrated inference which is configured but unusable due to the format bug. |
| **Pattern Compliance** | **PASS** | Clean architecture, proper DI, error handling, CORS, SSE streaming all follow good patterns. |
| **Overall** | **FAIL** | One P0 blocker remains: document ingestion does not work against the real Pinecone API. The fix is straightforward (change to NDJSON format) but until applied, the application cannot perform its core function. |

---

## Recommended Fix

In `PineconeService.StoreDocumentsAsync`, replace the JSON array serialization with NDJSON:

```csharp
// Current (WRONG):
var records = batch.Select(c => new Dictionary<string, object> { ... }).ToList();
var body = new { records };
var json = JsonSerializer.Serialize(body, JsonOptions);
// Content-Type: application/json

// Fixed (CORRECT):
var ndjson = string.Join("\n", batch.Select(c =>
    JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["_id"] = c.Id,
        ["chunk_text"] = c.Content,
        ["source"] = c.Source
    }, JsonOptions)));
// Content-Type: application/x-ndjson
```

Also update `PineconeServiceTests` to validate NDJSON output format instead of JSON array wrapper.

---

*Evaluation completed 2026-03-31. Previous 5 fixes verified. 1 new P0 found.*
