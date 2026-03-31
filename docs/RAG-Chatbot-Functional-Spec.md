# RAG Chatbot — Functional Specification

**Version:** 2.0
**Date:** 2026-03-29
**Status:** v3 — C#/.NET backend + Next.js frontend (evolved from TypeScript reference implementation)

This specification is language-agnostic and can be used to reimplement the RAG chatbot in any tech stack (e.g., C#/.NET + Next.js, Python + React, Go + Vue, etc.).

---

## 1. System Overview

### 1.1 Purpose

A Retrieval-Augmented Generation (RAG) chatbot that ingests documents, stores them as vector embeddings, and uses similarity search to provide grounded, cited answers from a large language model (LLM). Supports dual vector store backends (local and cloud) and optional LLM-powered query rewriting.

### 1.2 High-Level Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                        Frontend (SPA)                        │
│  Settings | Knowledge Base (per store) | Chat Interface      │
└────────────┬─────────────────────────────────────┬───────────┘
             │ REST + SSE                          │
┌────────────▼─────────────────────────────────────▼───────────┐
│                      Backend API Server                       │
│  /ingest  |  /ingest/sources  |  /chat  |  /config  | /health│
└────┬──────────┬──────────┬──────────┬──────────┬─────────────┘
     │          │          │          │          │
┌────▼────┐ ┌──▼───┐ ┌────▼────┐ ┌───▼───┐ ┌───▼────────────┐
│ Loaders │ │Split- │ │ Vector  │ │Query  │ │ LLM (Answer    │
│ PDF/Txt │ │ ter   │ │ Store   │ │Rewrite│ │ Generation)    │
│ /Web    │ │       │ │ Factory │ │(opt.) │ │                │
└─────────┘ └──────┘ └────┬────┘ └───────┘ └────────────────┘
                     ┌────┴────┐
                ┌────▼──┐ ┌───▼─────┐
                │ Local │ │  Cloud  │
                │ChromaDB│ │Pinecone│
                └───────┘ └────────┘
```

### 1.3 Two Core Pipelines

**Ingestion Pipeline:**
```
Document → Loader → Text Extraction → Chunking → Vector Store
```

**Chat Pipeline:**
```
User Question → [Query Rewrite] → Vector Search → Context Assembly → LLM Generation → Streamed Answer + Sources
```

---

## 2. Configuration

### 2.1 Environment Variables

All configuration is via environment variables. The system must support the following:

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `OPENAI_API_KEY` | Yes | — | API key for LLM (answer generation) and local embeddings |
| `PORT` | No | `3010` | Server listen port |
| `VECTOR_STORE` | No | `local` | Default vector store: `local` or `cloud` |
| `CHROMA_URL` | Local mode | `http://localhost:8000` | ChromaDB endpoint |
| `PINECONE_API_KEY` | Cloud mode | — | Pinecone API key |
| `QUERY_MODE` | No | `raw` | Default query processing: `raw` or `rewrite` |
| `REWRITE_LLM_BASE_URL` | Rewrite mode | — | API endpoint for query rewrite LLM |
| `REWRITE_LLM_MODEL` | Rewrite mode | — | Model name for query rewrite |
| `REWRITE_LLM_API_KEY` | Rewrite mode | — | API key for query rewrite LLM |

### 2.2 Runtime Overrides

The frontend can override `VECTOR_STORE` and `QUERY_MODE` per-request by passing `vectorStore` and `queryMode` parameters. If provided, these take precedence over environment defaults. If not provided, the server falls back to environment variables.

---

## 3. Document Ingestion

### 3.1 Supported Document Types

| Type | File Extensions | Extraction Method |
|------|----------------|-------------------|
| PDF | `.pdf` | PDF text extraction library (e.g., pdf-parse, PdfSharp, PyPDF) |
| Plain Text | `.txt` | Direct file read |
| Markdown | `.md` | Direct file read (treat as plain text) |
| Web Page | Any URL | HTML fetch + DOM text extraction (strip tags, scripts, styles) |

### 3.2 Ingestion Flow

1. **Input**: Receive file upload or URL
2. **Load**: Extract raw text from the document
   - For file uploads: determine type by file extension
   - For URLs: fetch HTML and extract visible text content
3. **Set Source Metadata**:
   - For URLs: use the URL as `source`
   - For uploaded files: use the **original filename** (not the server-side temp path) as `source`
4. **Split**: Chunk the text using recursive character splitting
   - Chunk size: **1000 characters**
   - Chunk overlap: **100 characters** (10% of chunk size)
   - Preserve source metadata on all chunks
5. **Validate**: If zero chunks produced, return an error
6. **Store**: Send chunks to the active vector store for embedding and storage
7. **Cleanup**: Delete temporary uploaded file from server

### 3.3 Document ID Generation

Each chunk stored in the vector database must have a unique ID. Use a pattern like:
```
doc_{timestamp}_{index}
```
Where `timestamp` is milliseconds since epoch and `index` is the chunk's position within the batch.

---

## 4. Vector Store Layer

### 4.1 Interface Contract

The vector store must be abstracted behind a common interface. All implementations must provide:

| Method | Parameters | Returns | Description |
|--------|-----------|---------|-------------|
| `storeDocuments` | documents[], vectorStore? | void | Embed and store document chunks |
| `similaritySearch` | query, topK?, vectorStore? | documents[] | Find semantically similar chunks |
| `resetCollection` | vectorStore? | void | Delete all data in the store |
| `listSources` | vectorStore? | string[] | Return unique source identifiers |

### 4.2 Factory Pattern

A factory function selects the implementation based on:
1. Per-request `vectorStore` parameter (highest priority)
2. `VECTOR_STORE` environment variable
3. Default: `local`

### 4.3 Local Implementation (ChromaDB)

| Setting | Value |
|---------|-------|
| Collection name | `rag-chatbot` |
| Distance metric | Cosine |
| Embedding model | OpenAI `text-embedding-3-small` |

**storeDocuments:**
- Generate embeddings for all chunk texts using OpenAI embedding API
- Store embeddings + text + metadata in ChromaDB collection

**similaritySearch:**
- Embed the query using the same embedding model
- Query ChromaDB with the embedding vector
- Default topK: 5
- Return matching documents with metadata

**listSources:**
- Retrieve all metadata from the collection (without embeddings/documents for efficiency)
- Extract unique `source` values
- Return deduplicated list

**resetCollection:**
- Delete collection if it exists (ignore errors if not found)
- Create fresh collection with cosine distance metric

### 4.4 Cloud Implementation (Pinecone)

| Setting | Value |
|---------|-------|
| Index name | `rag-chatbot-v3` |
| Namespace | `rag-chatbot` |
| Embedding model | `llama-text-embed-v2` (Pinecone integrated) |
| Cloud/Region | AWS us-east-1 (serverless) |

**storeDocuments:**
- Map documents to records with schema:
  - `_id`: unique identifier
  - `chunk_text`: document content (field mapped for integrated embedding)
  - `source`: source identifier string
- Batch upsert in groups of **96 records** per API call
- Pinecone handles embedding automatically via integrated model

**similaritySearch:**
- Send text query to Pinecone search API (Pinecone handles query embedding)
- Default topK: 5
- Map response hits to document objects with `pageContent` and `metadata.source`

**listSources:**
- Perform a broad search query (e.g., "document") with topK: 100
- Extract unique `source` values from results
- Note: This approach has a practical limit of ~100 unique sources

**resetCollection:**
- Delete all records in the namespace

### 4.5 Adding New Vector Store Implementations

To add a new vector store (e.g., Qdrant, Weaviate, pgvector):
1. Implement the interface contract (4.1)
2. Add a new value to the `VECTOR_STORE` config options
3. Register it in the factory

---

## 5. Query Processing

### 5.1 Query Modes

| Mode | Behavior |
|------|----------|
| `raw` (default) | User's question passed directly to vector search |
| `rewrite` | LLM rewrites question into a search-optimized query before vector search |

### 5.2 Query Rewrite Service

**Purpose:** Transform informal or imprecise user queries into search-optimized queries for better vector retrieval.

**Example transformations:**
- `"what's the RAG robot"` → `"RAG chatbot"`
- `"how does the thing store stuff in the database"` → `"how does the system store data in the database"`

**System prompt for rewrite LLM:**
```
You are a query rewriter for a document search system.
Your job is to take a user's natural language question and rewrite it into a clear, search-optimized query.

Rules:
- Extract the core intent and topic
- Expand abbreviations and acronyms
- Replace slang or informal terms with precise equivalents
- Remove conversational filler
- Output ONLY the rewritten query, nothing else — no quotes, no explanation
```

**API call specification:**
- Endpoint: `{REWRITE_LLM_BASE_URL}/chat/completions`
- Method: POST
- Headers: `Authorization: Bearer {REWRITE_LLM_API_KEY}`, `Content-Type: application/json`
- Body: `{ model, messages: [{role: "system", content: system_prompt}, {role: "user", content: question}], temperature: 0, max_tokens: 200 }`
- Parse: `response.choices[0].message.content.trim()`

**Provider agnostic:** The rewrite service uses the OpenAI-compatible chat completions API format. Any provider that supports this format (OpenAI, Azure OpenAI, Ollama, LM Studio, etc.) can be used.

**Graceful degradation:** If rewrite fails for any reason (config missing, API error, empty response), fall back silently to the raw query. Never block the user's question.

---

## 6. RAG Query & Answer Generation

### 6.1 Conversational Detection

Before performing vector search, check if the question is a conversational follow-up (referring to prior conversation, not documents). Detect by checking if the question contains phrases like:
- "you just said", "you mentioned"
- "summarise", "summarize"
- "what did you", "previous", "last answer"
- "above", "repeat"

If conversational AND chat history exists: skip vector search entirely, answer from conversation history only.

### 6.2 RAG Query Flow

1. **Check conversational**: If follow-up, use conversation-only prompt (skip steps 2-5)
2. **Rewrite query** (if `queryMode` = `rewrite`): Call query rewrite service
3. **Retrieve chunks**: Search vector store with processed query (topK = 5)
4. **Handle empty results**: If no chunks found, return "I couldn't find any relevant information in the knowledge base."
5. **Build context**: Number each chunk: `[1] chunk_text`, `[2] chunk_text`, etc.
6. **Build prompt**: Combine system instruction + context + conversation history + question
7. **Stream response**: Call LLM with streaming enabled, forward each chunk to the client
8. **Extract sources**: Collect unique `source` values from retrieved chunks

### 6.3 Answer Generation Prompt

```
You are a helpful assistant. Answer the question using the context provided below.
Focus on the core topic of the question and use any relevant information from the context to provide a helpful answer.
If the context is partially relevant, answer with what you can and note any gaps.
Only refuse to answer if the context has absolutely nothing to do with the question.

Context:
[1] chunk text here...
[2] chunk text here...

Conversation so far:
User: previous question
Assistant: previous answer

Question: current question
```

### 6.4 Conversational Prompt (no retrieval)

```
You are a helpful assistant. Based on the conversation below, answer the user's latest question.

Conversation:
User: ...
Assistant: ...

Question: current question
```

### 6.5 LLM Configuration

| Setting | Value |
|---------|-------|
| Model | `gpt-4o-mini` |
| Temperature | `0.2` |
| Streaming | Enabled |

### 6.6 Important Design Decision

The **rewritten query** is used only for vector search (retrieval). The **original user question** is always used in the LLM prompt (generation). This ensures the answer addresses what the user actually asked, not the search-optimized variant.

---

## 7. API Endpoints

### 7.1 POST /ingest

**Purpose:** Ingest a document (file upload or URL) into the knowledge base.

**Request (URL ingestion):**
```json
{
  "url": "https://example.com/article",
  "vectorStore": "local"  // optional override
}
```

**Request (file upload):**
- Content-Type: `multipart/form-data`
- Fields:
  - `file`: the uploaded file
  - `vectorStore`: optional override string

**Response (success):**
```json
{
  "success": true,
  "message": "Ingested file: report.pdf"
}
```

**Response (error):**
```json
{
  "error": "Ingestion failed",
  "detail": "Error message"
}
```

**Status codes:** 200 (success), 400 (missing input), 500 (server error)

**Behavior:**
- Determine document type by file extension (`.pdf` → PDF, `.txt`/`.md` → text)
- Store original filename as source metadata (not temp path)
- Delete temp file after processing

---

### 7.2 GET /ingest/sources

**Purpose:** List all unique ingested resources in a knowledge base.

**Query parameters:**
- `vectorStore` (optional): `local` or `cloud`

**Response:**
```json
{
  "success": true,
  "sources": [
    "report.pdf",
    "notes.md",
    "https://example.com/article"
  ]
}
```

---

### 7.3 DELETE /ingest/reset

**Purpose:** Clear all data from a knowledge base.

**Query parameters:**
- `vectorStore` (optional): `local` or `cloud`

**Response:**
```json
{
  "success": true,
  "message": "Knowledge base cleared."
}
```

---

### 7.4 POST /chat

**Purpose:** Send a question and receive a streamed answer with source citations.

**Request:**
```json
{
  "question": "What is RAG?",
  "history": [
    { "role": "user", "content": "previous question" },
    { "role": "assistant", "content": "previous answer" }
  ],
  "vectorStore": "local",   // optional override
  "queryMode": "raw"         // optional override
}
```

**Response:** Server-Sent Events (SSE) stream

```
Content-Type: text/event-stream
Cache-Control: no-cache
Connection: keep-alive

data: {"type":"chunk","text":"Retrieval"}

data: {"type":"chunk","text":"-augmented"}

data: {"type":"chunk","text":" generation is..."}

data: {"type":"sources","sources":["report.pdf","https://example.com"]}

data: {"type":"done"}
```

**SSE Event Types:**

| Type | Payload | Description |
|------|---------|-------------|
| `chunk` | `{type: "chunk", text: string}` | Incremental answer text |
| `sources` | `{type: "sources", sources: string[]}` | Source documents used |
| `done` | `{type: "done"}` | Stream complete |

**Status codes:** 200 (streaming), 400 (missing question), 500 (error)

---

### 7.5 GET /config

**Purpose:** Return current server configuration for the frontend to initialize UI state.

**Response:**
```json
{
  "vectorStore": "local",
  "queryMode": "raw",
  "rewriteLlm": {
    "baseUrl": "https://api.openai.com/v1",
    "model": "gpt-4o-mini"
  }
}
```

**Security:** Never expose API keys in this response.

---

### 7.6 GET /health

**Purpose:** Simple health check.

**Response:**
```json
{
  "status": "ok"
}
```

---

## 8. Frontend Specification

### 8.1 Layout

```
┌────────────────────────────────────────────────────────┐
│ Header: App Name + Subtitle + Status Indicator         │
├──────────────┬─────────────────────────────────────────┤
│   Sidebar    │            Chat Area                    │
│   (300px)    │                                         │
│              │  ┌─────────────────────────────────┐    │
│  Settings    │  │                                 │    │
│  ──────────  │  │     Message History             │    │
│  Store: [▼]  │  │     (scrollable)                │    │
│  Query: [▼]  │  │                                 │    │
│  [LLM info]  │  │  User:  ●●●●●●●●●●             │    │
│              │  │  Bot:   ●●●●●●●●●●●●●●●        │    │
│  ──────────  │  │         Sources: [links]        │    │
│  KB Panel    │  │                                 │    │
│  (per store) │  └─────────────────────────────────┘    │
│              │  ┌─────────────────────────────────┐    │
│              │  │ [Question input...] [Send]      │    │
│              │  └─────────────────────────────────┘    │
└──────────────┴─────────────────────────────────────────┘
```

### 8.2 Settings Section

**Store Dropdown:**
- Options: "Local (ChromaDB)", "Cloud (Pinecone)"
- On change: switch the visible Knowledge Base panel
- Value sent with every `/ingest` and `/chat` request

**Query Dropdown:**
- Options: "Raw", "Enhancement"
- On change: toggle LLM info display
- When "Enhancement" selected: show configured LLM model name and API base URL (loaded from `GET /config`)
- Value sent with every `/chat` request

### 8.3 Knowledge Base Panels

**Two independent panels** — one for Local, one for Cloud. Only the panel matching the Store dropdown selection is visible. Each panel contains:

1. **Section Header**: "Knowledge Base — Local" or "Knowledge Base — Cloud"

2. **URL Ingestion**:
   - Text input (placeholder: URL example)
   - "Add URL" button
   - On click: POST to `/ingest` with `{url, vectorStore}`

3. **File Upload**:
   - File input (accepts `.pdf`, `.txt`, `.md`)
   - "Upload File" button
   - On click: POST to `/ingest` with FormData containing file + vectorStore

4. **Activity Log**:
   - Scrollable log area
   - Each entry prefixed with bullet
   - Color-coded: info (blue), success (green), error (red)
   - Initial message: "Ready. Add documents to get started."
   - Logs are independent per store panel

5. **List Resources Button**:
   - On click: GET `/ingest/sources?vectorStore={store}`
   - Displays results in a scrollable list below the button
   - Shows "No resources found." if empty
   - Shows "Loading..." while fetching

6. **Clear Knowledge Base Button**:
   - Styled as danger/destructive action
   - On click: confirmation dialog first
   - Confirm message should identify which store will be cleared
   - DELETE `/ingest/reset?vectorStore={store}`
   - Also clears chat history on success

### 8.4 Chat Interface

**Message Display:**
- Scrollable message area
- Empty state: icon + "Ask a question about your documents"
- User messages: right-aligned, colored bubble (e.g., indigo/blue)
- Bot messages: left-aligned, white/light bubble with subtle shadow
- Source citations displayed below bot message as clickable links

**Thinking Indicator:**
- Animated dots (3 bouncing dots) shown while waiting for first chunk
- Removed when first chunk arrives

**Input Bar:**
- Text input with placeholder "Ask a question..."
- Send button
- Enter key triggers send
- Both input and button disabled during streaming
- Re-enabled and focused after response completes

**Chat History:**
- Maintained in browser memory as array of `{role, content}` objects
- Sent with each `/chat` request for multi-turn conversation
- Cleared on page refresh (not persisted)
- Cleared when knowledge base is reset

### 8.5 SSE Stream Handling

1. POST to `/chat` with question, history, vectorStore, queryMode
2. Read response body as a stream
3. Buffer incoming data, split by newlines
4. For each line starting with `data: `:
   - Parse JSON payload
   - `type: "chunk"` → append text to bot message bubble
   - `type: "sources"` → render source links below message (if any)
   - `type: "done"` → mark stream complete
5. After stream ends: add user question and full answer to chat history
6. On error: remove thinking indicator, show error message as bot message

### 8.6 Initialization

On page load:
1. Fetch `GET /config` from server
2. Set Store dropdown to server's `vectorStore` value
3. Set Query dropdown to server's `queryMode` value
4. Toggle KB panel visibility based on store value
5. Update LLM info display based on query mode
6. If fetch fails: use defaults (local, raw)

---

## 9. Document Processing Details

### 9.1 PDF Loading

- Read file as binary buffer
- Extract text using PDF parsing library
- Return single document with:
  - Content: all extracted text
  - Metadata: `{source: original_filename, pages: page_count}`

### 9.2 Text/Markdown Loading

- Read file as UTF-8 text
- Return document(s) with:
  - Content: file text content
  - Metadata: `{source: original_filename}`

### 9.3 Web Page Loading

- Fetch HTML from URL
- Parse DOM and extract visible text (strip scripts, styles, navigation)
- Return document(s) with:
  - Content: extracted text
  - Metadata: `{source: url}`

### 9.4 Text Splitting

- Algorithm: Recursive character text splitting
- Separators (in order): `\n\n`, `\n`, ` `, ``
- Chunk size: 1000 characters
- Chunk overlap: 100 characters
- Metadata from parent document preserved on all chunks

---

## 10. Error Handling

### 10.1 Principles

- All errors should be caught and returned as structured JSON responses
- Server-side errors should be logged to console with context
- Client-facing errors should include error type and detail message
- Streaming errors should be handled gracefully (client may receive partial response)

### 10.2 Graceful Degradation

| Scenario | Behavior |
|----------|----------|
| Query rewrite config missing | Fall back to raw query, log warning |
| Query rewrite API fails | Fall back to raw query, log warning |
| Query rewrite returns empty | Fall back to raw query, log warning |
| Vector store returns 0 results | Return "couldn't find relevant information" message |
| Empty document uploaded | Return error "No content could be extracted" |
| Missing question in chat request | Return HTTP 400 |
| Missing file and URL in ingest | Return HTTP 400 |
| Vector store connection failure | Return HTTP 500 with detail |

### 10.3 File Cleanup

Temporary uploaded files must always be deleted after processing, even if ingestion fails. Use try/finally or equivalent pattern.

---

## 11. Data Models

### 11.1 Document

```
Document {
  pageContent: string     // The text content of the chunk
  metadata: {
    source: string        // Original filename or URL
    pages?: number        // Page count (PDF only)
    [key: string]: any    // Additional metadata preserved through pipeline
  }
}
```

### 11.2 Chat Message

```
Message {
  role: "user" | "assistant"
  content: string
}
```

### 11.3 SSE Event

```
ChunkEvent   { type: "chunk",   text: string }
SourcesEvent { type: "sources", sources: string[] }
DoneEvent    { type: "done" }
```

### 11.4 Server Config

```
Config {
  vectorStore: string           // "local" or "cloud"
  queryMode: string             // "raw" or "rewrite"
  rewriteLlm: {
    baseUrl: string             // API endpoint (no key exposed)
    model: string               // Model name
  }
}
```

---

## 12. Non-Functional Requirements

### 12.1 Performance

- Streaming responses must start within 2-3 seconds of question submission
- Query rewrite adds ~200-400ms latency (acceptable trade-off)
- File upload size limited by server configuration (default: reasonable limit)
- Batch upsert for vector stores to avoid API limits

### 12.2 Security

- API keys must never be exposed to the frontend (only model name and base URL in `/config`)
- File uploads should be stored temporarily and cleaned up after processing
- CORS should be configured appropriately for the deployment environment
- `.env` file must never be committed to version control

### 12.3 Extensibility

The system is designed for easy extension:
- **New vector stores**: Implement the VectorStore interface and register in factory
- **New document types**: Add a new loader function and register the file extension
- **New query modes**: Add processing logic to the query processor
- **New LLM providers**: The answer generation and query rewrite are already provider-configurable

### 12.4 Statelessness

- Server is stateless — no session storage
- Chat history lives in the browser and is sent with each request
- Vector stores provide persistence
- Configuration is via environment variables

---

## 13. Testing Specification

### 13.1 Test Categories

| Test | Purpose | Parameters |
|------|---------|------------|
| Ingest Local | Verify document ingestion into ChromaDB | filename or URL |
| Ingest Cloud | Verify document ingestion into Pinecone | filename or URL |
| RAG Local + Raw | Verify retrieval and answer from ChromaDB with raw query | question |
| RAG Local + Rewrite | Verify retrieval with query rewriting on ChromaDB | question |
| RAG Cloud + Raw | Verify retrieval and answer from Pinecone with raw query | question |
| RAG Cloud + Rewrite | Verify retrieval with query rewriting on Pinecone | question |
| List Sources Local | Verify source listing from ChromaDB | — |
| List Sources Cloud | Verify source listing from Pinecone | — |

### 13.2 Test Environment Setup

Tests should support configuring:
- `vectorStore`: which backend to test against
- `queryMode`: which query processing to use

These should be set before importing service modules to ensure correct initialization.

### 13.3 Test Data

Include a sample markdown file (e.g., `test-sample.md`) in a `documents/` folder with known content about the system itself. This provides a predictable baseline for testing retrieval accuracy.

---

## 14. Appendix

### 14.1 Vector Store Comparison

| Feature | ChromaDB (Local) | Pinecone (Cloud) |
|---------|-------------------|-------------------|
| Deployment | Docker container | Managed cloud service |
| Embedding | External (OpenAI) | Integrated (llama-text-embed-v2) |
| Cost | Free (self-hosted) | Free tier + pay-as-you-go |
| Scaling | Manual | Automatic (serverless) |
| Persistence | Docker volume | Cloud-managed |
| Setup | Docker required | API key only |

### 14.2 Query Rewrite Value

| User Query | Without Rewrite | With Rewrite |
|------------|----------------|--------------|
| "what's the RAG robot" | May miss "RAG chatbot" content | Rewrites to "RAG chatbot" → better match |
| "how does the thing store stuff" | Vague terms reduce match quality | Rewrites to precise terms |
| "tell me about embeddings" | Decent match | Cleaned to "embeddings vector storage" |

### 14.3 SSE Protocol Notes

- Each event is a line starting with `data: ` followed by JSON
- Events are separated by double newline (`\n\n`)
- Client must buffer partial reads and split by newlines
- The `done` event signals the client to stop reading
- Error handling: if the stream breaks mid-response, the client should display what was received

### 14.4 File Upload Handling

- Server accepts `multipart/form-data`
- Files are saved to a temporary directory with random names
- Original filename is extracted from the upload metadata
- Original filename is stored as `source` in vector metadata
- Temp file is deleted after processing (success or failure)
