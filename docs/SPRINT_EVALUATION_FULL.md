# RAG Chatbot v3 -- Full Sprint Evaluation Report

**Date:** 2026-03-31
**Evaluator:** QA Specialist Agent
**Scope:** All sprints B1-B5 (Backend), F1-F4 (Frontend)
**Backend URL:** http://localhost:3010
**Frontend URL:** http://localhost:3000

---

## Executive Summary

**Overall Verdict: FAIL -- Critical blocking issues prevent end-to-end functionality**

The application has strong code quality (builds clean, all 137 tests pass, zero lint errors) and a well-structured frontend layout. However, two critical issues prevent the core RAG pipeline from functioning:

1. **Pinecone index misconfiguration** -- Integrated inference is not configured on the `rag-chatbot-v3` index, causing all vector store operations (ingest, search, list sources, reset) to fail with HTTP 400.
2. **Frontend config fetch failure** -- The `useConfig` hook requires `NEXT_PUBLIC_API_URL` env var but no `.env.local` file exists, causing "Disconnected" status and "Unavailable" LLM info display.

---

## 1. Correctness (FAIL -- hard gate)

### Backend Endpoints

| Test | Expected | Actual | Verdict |
|------|----------|--------|---------|
| `GET /health` | `{"status":"ok"}` HTTP 200 | `{"status":"ok"}` HTTP 200 | PASS |
| `GET /config` | rewriteLlm info, no API keys | `{"rewriteLlm":{"baseUrl":"https://api.openai.com/v1","model":"gpt-4o-mini"}}` -- no keys exposed | PASS |
| `POST /ingest` (file upload) | Success response | HTTP 500: `Pinecone upsert failed...Integrated inference is not configured` | **FAIL** |
| `POST /ingest` (URL) | Success response | HTTP 500: same Pinecone error | **FAIL** |
| `POST /ingest` (no file/URL) | HTTP 400 | HTTP 400: `No file or URL provided...` | PASS |
| `GET /ingest/sources` | Source list | HTTP 500: `Pinecone search failed...Integrated inference is not configured` | **FAIL** |
| `DELETE /ingest/reset` | Success response | HTTP 500: `Pinecone delete failed with status 404: Namespace not found` | **FAIL** |
| `POST /chat` (valid question) | SSE stream | HTTP 500 with empty body | **FAIL** |
| `POST /chat` (empty question) | HTTP 400 | HTTP 400: `Missing question` | PASS |

**Root Cause:** The Pinecone index `rag-chatbot-v3` (host: `rag-chatbot-v3-y3gph8e.svc.aped-4627-b74a.pinecone.io`) does not have integrated inference enabled. The code correctly uses the `/records/namespaces/{ns}/upsert` and `/records/namespaces/{ns}/search` endpoints (which require integrated embeddings), but the index itself was not created with this feature. This is an infrastructure configuration issue, not a code bug.

**Secondary Issue:** The `ResetCollectionAsync` method does not gracefully handle the case where the namespace doesn't exist (Pinecone returns 404). Per the spec (section 10.2), this should succeed silently.

### Frontend End-to-End

| Test | Expected | Actual | Verdict |
|------|----------|--------|---------|
| Page loads | No errors | Loads, but shows "Disconnected" and "Unavailable" | **PARTIAL** |
| Chat send question | User message + bot response | User message appears, bot shows "Error: Failed to fetch" | **FAIL** |
| Chat source citations | Sources below bot message | Not testable (chat fails) | **BLOCKED** |
| Chat streaming | Thinking indicator, streamed text | Thinking indicator appears briefly, then error | **FAIL** |

### Blocking Issues

1. **[CRITICAL] Pinecone integrated inference not configured** -- All vector store operations fail. The Pinecone index must be recreated with integrated inference enabled for `llama-text-embed-v2`.

2. **[CRITICAL] Frontend `useConfig` hook missing fallback** -- `useConfig.ts` line 27 checks `if (!apiUrl)` and sets status to "disconnected" when `NEXT_PUBLIC_API_URL` is not set. Meanwhile, `api.ts` has a fallback `|| "http://localhost:3010"`. The inconsistency means config always fails in local dev without `.env.local`.

3. **[HIGH] `.env.local.example` has wrong port** -- Shows `NEXT_PUBLIC_API_URL=http://localhost:5000` but backend runs on port 3010.

4. **[MEDIUM] ChatController has no try-catch** -- The `Post` method in `ChatController.cs` wraps the SSE streaming in no error handler. When Pinecone throws, the response is HTTP 500 with a completely empty body (Content-Length: 0). The frontend's `chatApi.ts` tries to parse the error JSON but gets nothing, resulting in "Failed to fetch".

5. **[MEDIUM] Reset does not handle empty namespace** -- `PineconeService.ResetCollectionAsync` throws on 404 (namespace not found). Should treat "not found" as a successful no-op.

---

## 2. Code Quality (PASS -- hard gate)

### Backend

| Check | Result |
|-------|--------|
| `dotnet build` | 0 errors, 0 warnings |
| `dotnet test` | 137 passed, 0 failed, 0 skipped |

### Frontend

| Check | Result |
|-------|--------|
| `npx tsc --noEmit` | 0 errors |
| `npm run lint` | 0 warnings, 0 errors |

**Verdict: PASS** -- Both codebases are clean. The 137 unit tests demonstrate good coverage of business logic. The frontend TypeScript compilation and linting are flawless.

---

## 3. Spec Fidelity (WARN)

### Matches Spec

- Health endpoint response format matches spec section 7.6
- Config endpoint matches spec section 7.5 (returns rewriteLlm, no keys)
- Ingest error (missing input) returns HTTP 400 per spec section 7.1
- Chat error (missing question) returns HTTP 400 per spec section 7.4
- SSE event types (chunk/sources/done) are implemented per spec section 7.4
- RAG pipeline flow matches spec section 6.2 (conversational detection, rewrite, search, context assembly, streaming)
- Query rewrite uses original question for LLM prompt per spec section 6.6 and ADR-003
- Document chunking configured at 1000 chars / 100 overlap per spec section 3.2 and ADR-009
- Batch upsert at 96 records per ADR-001 / spec section 4.4
- Frontend layout matches spec section 8.1 (header + sidebar + chat area)
- Knowledge base panel has all spec 8.3 elements (URL input, file upload, activity log, list resources, clear KB)
- Chat interface has all spec 8.4 elements (empty state, input, send button, disabled during streaming)
- SSE stream handling in `chatApi.ts` correctly implements spec section 8.5

### Deviations from Spec

1. **No `vectorStore`/`queryMode` parameters** -- Per ADR-001 and ADR-003, these were intentionally removed (Pinecone-only, always-on rewrite). This is a valid architectural decision documented in DECISIONS.md, but deviates from the original functional spec sections 2.2, 5.1, 7.4.

2. **No dual KB panels** -- Spec section 8.3 calls for two panels (Local/Cloud). Per ADR-001, only one panel exists (Pinecone). Consistent with the architectural decision.

3. **No Store/Query dropdowns** -- Spec section 8.2 describes these. Intentionally omitted per ADR-001/ADR-003.

4. **Chat error response is empty body** -- Spec section 10.1 says "All errors should be caught and returned as structured JSON responses." The chat endpoint returns HTTP 500 with no body when vector search fails.

5. **Reset does not handle empty namespace gracefully** -- Spec section 4.4 says resetCollection should "delete all records in the namespace." It should not throw if the namespace is already empty/doesn't exist.

---

## 4. Pattern Compliance (WARN)

### ADR Compliance

| ADR | Status | Notes |
|-----|--------|-------|
| ADR-001: Pinecone Only | PASS | No ChromaDB, no interface abstraction, no factory pattern |
| ADR-002: SSE Streaming | PASS | Correct event types (chunk/sources/done), `text/event-stream` content type |
| ADR-003: Query Rewrite Always On | PASS | Always rewrites, original question used in LLM prompt |
| ADR-004: API Keys Never Exposed | PASS | Config endpoint returns only model name and baseUrl |
| ADR-005: Context Reset Harness | N/A | Agent workflow concern, not code |
| ADR-006: Generator-Evaluator Separation | PASS | This evaluation follows the pattern |
| ADR-007: Sprint Contracts | N/A | Process concern |
| ADR-008: Tech Stack | PASS | C#/.NET 9, ASP.NET Core, Next.js, Tailwind, Pinecone |
| ADR-009: Markdown-Aware Chunking | WARN | Not verified due to Pinecone failure |
| ADR-010: MVP File Types | PASS | Supports MD, TXT, URL only |
| ADR-011: No Docker | PASS | No Docker files present |
| ADR-012: CORS Allow Any Origin | PASS | `AllowAnyOrigin()` configured in Program.cs |
| ADR-013: 3-Strike Rule | N/A | Process concern |

### Architecture Assessment

- **Clean Architecture:** Backend has proper Api/Core/Infrastructure separation
- **Dependency Injection:** Services properly registered and injected
- **Interface segregation:** `IPineconeService`, `IIngestionService`, `IRagPipelineService`, `IQueryRewriteService`, `ILlmService`, `IConversationalDetector`
- **Frontend component structure:** Hooks (`useConfig`, `useChat`, `useKnowledgeBase`), services (`api.ts`, `chatApi.ts`), clear separation of concerns

---

## Evidence (Screenshots)

| File | Description |
|------|-------------|
| `playwright_screenshots/01-initial-load.png` | Initial page load -- shows "Disconnected" status, "Unavailable" LLM info |
| `playwright_screenshots/02-sidebar-kb.png` | Sidebar with KB panel -- all elements present |
| `playwright_screenshots/03-chat-empty.png` | Empty state with "Ask a question about your documents" |
| `playwright_screenshots/04-chat-question-typed.png` | Question typed in input |
| `playwright_screenshots/05-chat-message-sent.png` | Message sent, user bubble appears |
| `playwright_screenshots/06-chat-response.png` | Error response: "Error: Failed to fetch" |
| `playwright_screenshots/07-chat-final.png` | Final state showing error |

---

## Required Fixes (Priority Order)

### P0 -- Must Fix Before Re-evaluation

1. **Recreate Pinecone index with integrated inference** -- The index `rag-chatbot-v3` must be configured with `llama-text-embed-v2` integrated embeddings. This is an infrastructure task, not a code change.

2. **Create `frontend/.env.local`** with correct content:
   ```
   NEXT_PUBLIC_API_URL=http://localhost:3010
   ```

3. **Fix `useConfig.ts`** -- Add fallback URL when `NEXT_PUBLIC_API_URL` is not set, matching the pattern in `api.ts`:
   ```typescript
   const apiUrl = process.env.NEXT_PUBLIC_API_URL || "http://localhost:3010";
   ```

4. **Fix `.env.local.example`** -- Change port from 5000 to 3010.

### P1 -- Should Fix

5. **Add try-catch to `ChatController.Post`** -- Wrap the SSE streaming loop in error handling so Pinecone failures return a structured error (either as a JSON response before streaming starts, or as an SSE error event).

6. **Handle 404 in `PineconeService.ResetCollectionAsync`** -- Treat "namespace not found" (404) as a successful no-op rather than throwing.

### P2 -- Nice to Have

7. **Consider adding a health check that validates Pinecone connectivity** -- Currently `/health` always returns `ok` even when Pinecone is misconfigured. Adding a Pinecone ping would help diagnose infrastructure issues earlier.

---

## Summary Scorecard

| Criterion | Verdict | Details |
|-----------|---------|---------|
| **Correctness** | **FAIL** | 5 of 9 backend endpoints fail; frontend chat flow broken |
| **Code Quality** | **PASS** | 0 build errors, 137 tests pass, 0 lint errors |
| **Spec Fidelity** | **WARN** | Spec deviations are intentional (ADR), but error handling gaps exist |
| **Pattern Compliance** | **WARN** | ADRs followed correctly; missing error handling in ChatController |

**Overall: FAIL -- Re-evaluate after P0 fixes are applied.**
