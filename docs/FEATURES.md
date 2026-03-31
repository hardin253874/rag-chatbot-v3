# RAG Chatbot v3 — Feature Checklist

Canonical feature list. Updated after brainstorm to reflect simplified scope (Pinecone only, always-on rewrite).

---

## Backend — API Endpoints

- [x] `GET /health` — health check returning `{"status":"ok"}`
- [x] `GET /config` — return server config (rewriteLlm info) without secrets
- [x] `POST /ingest` — file upload ingestion (MD, TXT via multipart/form-data)
- [x] `POST /ingest` — URL ingestion (fetch HTML, extract text)
- [x] `GET /ingest/sources` — list unique ingested sources
- [x] `DELETE /ingest/reset` — clear all data from knowledge base
- [x] `POST /chat` — RAG query with SSE streaming response

## Backend — Document Processing

- [x] Text/Markdown loader — read UTF-8 text files
- [x] Markdown-aware splitter — split by heading boundaries for `.md` files
- [x] Web page loader — fetch URL, parse HTML, extract visible text
- [x] Recursive character splitter — for TXT and URL content (configurable CHUNK_SIZE, CHUNK_OVERLAP)
- [x] Document ID generation — `doc_{timestamp}_{index}` pattern
- [x] Source metadata — original filename (not temp path) stored as `source`
- [x] Temp file cleanup — always delete after processing (success or failure)

## Backend — Pinecone Integration

- [x] Pinecone client — connect to `rag-chatbot-v3` index, `rag-chatbot` namespace
- [x] Store documents — upsert records with `chunk_text` field, batch size 96
- [x] Similarity search — text query, top 5 results
- [x] List sources — retrieve unique source values
- [x] Reset collection — delete all records in namespace

## Backend — Query Processing

- [x] Query rewrite service — LLM rewrites question for better retrieval (always on)
- [x] Rewrite graceful degradation — fall back to original query on any failure
- [x] Rewrite uses OpenAI-compatible chat completions API format

## Backend — RAG Pipeline

- [x] Conversational detection — detect follow-up questions, skip vector search
- [x] Vector search — retrieve top 5 chunks from Pinecone
- [x] Context assembly — number chunks `[1]`, `[2]`, etc.
- [x] LLM answer generation — GPT-4o-mini, temperature 0.2, streaming
- [x] SSE streaming — event types: chunk, sources, done
- [x] Source extraction — unique source values from retrieved chunks
- [x] Rewritten query for search only, original question for LLM prompt

## Backend — Configuration & Infrastructure

- [x] Environment variable loading (OPENAI_API_KEY, PINECONE_API_KEY, PORT, REWRITE_LLM_*, CHUNK_SIZE, CHUNK_OVERLAP)
- [x] CORS — allow any origin
- [x] Clean Architecture scaffold — Api, Core, Infrastructure projects

## Frontend — Layout & Structure

- [x] Main layout — header + sidebar + chat area
- [x] Header — app name, subtitle, status indicator
- [x] Modern SaaS dashboard styling (per Designer spec)

## Frontend — Settings Section

- [x] LLM info display — show rewrite model name and base URL (from GET /config)
- [x] Initialization from `GET /config` response
- [x] Fallback to defaults if config fetch fails

## Frontend — Knowledge Base Panel

- [x] Single KB panel (Pinecone)
- [x] URL ingestion — text input + "Add URL" button → POST /ingest
- [x] File upload — file input (.md, .txt) + "Upload File" button → POST /ingest
- [x] Activity log — scrollable, color-coded (info blue, success green, error red)
- [x] List Resources button — GET /ingest/sources, scrollable results list
- [x] Clear Knowledge Base button — confirmation dialog, DELETE /ingest/reset
- [x] Clear KB — also clears chat history on success

## Frontend — Chat Interface

- [x] Message display — scrollable message area
- [x] Empty state — icon + "Ask a question about your documents"
- [x] User messages — right-aligned, styled bubble
- [x] Bot messages — left-aligned, styled bubble
- [x] Source citations — clickable links below bot messages
- [x] Thinking indicator — animated dots, removed when first chunk arrives
- [x] Input bar — text input + send button
- [x] Enter key triggers send
- [x] Input and button disabled during streaming
- [x] Re-enabled and focused after response completes

## Frontend — SSE Stream Handling

- [x] POST to /chat with question and history
- [x] Read response body as ReadableStream
- [x] Parse `data: ` lines as JSON
- [x] Handle `chunk` event — append text to bot message
- [x] Handle `sources` event — render source links
- [x] Handle `done` event — mark stream complete
- [x] Add user question and full answer to chat history after stream
- [x] Error handling — remove thinking indicator, show error as bot message

## Frontend — Chat History

- [x] Maintained in browser memory as `{role, content}[]`
- [x] Sent with each /chat request
- [x] Cleared on page refresh (not persisted)
- [x] Cleared when knowledge base is reset

## Frontend — Initialization

- [x] Fetch GET /config on page load
- [x] Update LLM info display
- [x] Fallback to defaults if fetch fails

## Infrastructure

- [x] .env.example with all variables
- [x] .gitignore
- [x] documents/test-sample.md with known content
