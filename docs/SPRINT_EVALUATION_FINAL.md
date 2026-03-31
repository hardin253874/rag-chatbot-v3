# Sprint Evaluation - Final (Re-evaluation #2)

**Date**: 2026-03-31
**Evaluator**: QA Specialist Agent
**Sprint**: Backend MVP (C#/.NET RAG Chatbot v3)

---

## Bugs Found and Fixed During Evaluation

Two additional bugs were discovered and fixed during this evaluation:

### Bug #6: OpenAI HttpClient Missing BaseAddress
- **File**: `backend/RagChatbot.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs`
- **Symptom**: Chat endpoint returned `"Error: An invalid request URI was provided. Either the request URI must be an absolute URI or BaseAddress must be set."`
- **Root Cause**: The named HttpClient `"OpenAI"` was registered with `services.AddHttpClient("OpenAI")` without setting a `BaseAddress`. The `LlmService` uses a relative URI (`/v1/chat/completions`), which requires the HttpClient to have a `BaseAddress`.
- **Fix**: Changed registration to `services.AddHttpClient("OpenAI", client => { client.BaseAddress = new Uri("https://api.openai.com"); });`

### Bug #7: .env File Path Resolution Off by One Level
- **File**: `backend/RagChatbot.Api/Program.cs`
- **Symptom**: Chat endpoint returned 401 from OpenAI ("You didn't provide an API key") because `OPENAI_API_KEY` was not loaded.
- **Root Cause**: The `.env` path was `Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".env")` which resolved to `backend/.env`. The actual `.env` file is at the project root (`rag-chatbot-v3/.env`), one level higher. `AppContext.BaseDirectory` = `backend/RagChatbot.Api/bin/Debug/net9.0/`, so 4 levels up = `backend/`, but 5 levels up = `rag-chatbot-v3/`.
- **Fix**: Changed to `Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".env")` (5 levels up).

---

## Evaluation Criteria

### 1. Functional Completeness: PASS

All API endpoints tested and working:

| Test | Result | Evidence |
|------|--------|----------|
| `POST /ingest` (file upload) | PASS | `{"success":true,"message":"Ingested file: test-sample.md"}` |
| `GET /ingest/sources` | PASS | `{"success":true,"sources":["test-sample.md"]}` |
| `POST /chat` (RAG query) | PASS | SSE stream with chunk events, sources event (`test-sample.md`), done event |
| `DELETE /ingest/reset` | PASS | `{"success":true,"message":"Knowledge base cleared."}` |
| `GET /config` | PASS | Returns `{"rewriteLlm":{"baseUrl":"...","model":"gpt-4o-mini"}}` |
| `GET /health` | PASS | `{"status":"ok"}` |

Full RAG pipeline verified:
- Document ingested into Pinecone via NDJSON format
- Vector search returns relevant chunks from ingested document
- LLM streams grounded response with citations
- Sources correctly attributed to `test-sample.md`

### 2. Code Quality: PASS

| Check | Result |
|-------|--------|
| `dotnet build` | 0 errors, 0 warnings |
| `dotnet test` | 137/137 passed, 0 failed, 0 skipped |
| `npx tsc --noEmit` (frontend) | 0 errors |
| `npm run lint` (frontend) | 0 ESLint warnings or errors |

### 3. Frontend Integration: PASS

Playwright automated test verified full user workflow:

| Step | Result | Evidence |
|------|--------|----------|
| Page loads at localhost:3000 | PASS | Screenshot `eval-01-page-load.png` |
| Status shows "Connected" (green dot) | PASS | Header shows green dot + "Connected" |
| Settings panel shows LLM config | PASS | Model: gpt-4o-mini, Base URL: https://api.openai.com/v1 |
| Upload test-sample.md | PASS | Activity log: "Uploading test-sample.md..." then "Ingested file: test-sample.md" |
| Type question in chat input | PASS | Screenshot `eval-03-question-typed.png` |
| Send question (Enter) | PASS | User message bubble appears |
| Bot response streams in | PASS | Full RAG response about RAG chatbots |
| Sources displayed | PASS | "test-sample.md" shown as source |

Screenshots saved to: `playwright_screenshots/eval-01-page-load.png` through `eval-05-final.png`

### 4. End-to-End Pipeline: PASS

The complete data flow works:
1. Document upload -> markdown chunking -> Pinecone upsert (NDJSON format)
2. Chat question -> vector search (top 5 chunks) -> context assembly -> LLM streaming -> SSE response
3. Frontend receives SSE events -> displays streaming text -> shows sources

---

## Summary of All Bugs Found Across Evaluations

| # | Bug | Severity | Status |
|---|-----|----------|--------|
| 1 | PineconeService NDJSON format (JSON array vs line-delimited) | Critical | Fixed |
| 2 | (Previous evaluation bugs) | Various | Fixed |
| 3 | (Previous evaluation bugs) | Various | Fixed |
| 4 | (Previous evaluation bugs) | Various | Fixed |
| 5 | (Previous evaluation bugs) | Various | Fixed |
| 6 | OpenAI HttpClient missing BaseAddress | Critical | Fixed (this eval) |
| 7 | .env file path off by one directory level | Critical | Fixed (this eval) |

---

## Overall: PASS

The RAG Chatbot v3 backend MVP is fully functional end-to-end. All API endpoints work correctly, the full ingestion-to-chat pipeline operates as designed, the frontend integrates seamlessly, and all 137 unit tests pass with zero build errors or lint warnings.

The two critical bugs found during this evaluation (missing OpenAI HttpClient BaseAddress and .env path resolution) were integration issues that only manifested at runtime with a live OpenAI API call -- they were not detectable by unit tests alone since the tests mock HTTP clients with explicit BaseAddress values.
