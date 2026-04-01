# RAG Chatbot v3

A full-stack **Agentic RAG** chatbot that ingests documents, stores them as vector embeddings in Pinecone, and uses an LLM-driven agent loop to provide grounded, cited answers via OpenAI GPT-4o-mini with real-time streaming. Built with a C#/.NET 9 backend (Clean Architecture) and a Next.js 14 frontend styled as a modern SaaS dashboard.

Unlike traditional RAG (single-pass retrieve-then-answer), **Agentic RAG** gives the LLM ownership of the retrieval loop. The agent decides when to search, evaluates whether results are sufficient, reformulates queries when needed, and only answers when it has enough context -- up to 3 retrieval iterations per question.


## Tech Stack

| Layer | Technology |
|-------|------------|
| Backend | C# / .NET 9 / ASP.NET Core Web API / Clean Architecture |
| Frontend | Next.js 14 / TypeScript / Tailwind CSS |
| Vector Store | Pinecone (serverless, AWS us-east-1, integrated llama-text-embed-v2 embeddings) |
| LLM | OpenAI GPT-4o-mini (agentic reasoning, answer generation, query rewriting, smart chunking, quality evaluation) |
| Agent Framework | Custom agentic loop with OpenAI function calling (tool_use) |
| Re-ranking | Pinecone Rerank API (bge-reranker-v2-m3) |
| Markdown | react-markdown + @tailwindcss/typography |
| Design | Modern SaaS dashboard: dark sidebar, light content area, indigo accent |


## Architecture

The backend follows **Clean Architecture** with three projects:

- **RagChatbot.Api** -- ASP.NET Core controllers, middleware, program entry point
- **RagChatbot.Core** -- domain models, interfaces, configuration (no external dependencies)
- **RagChatbot.Infrastructure** -- implementations: Pinecone client, LLM service, document processing, query rewrite

### Core Pipelines

**Ingestion Pipeline:**

```
Document --> Loader (MD/TXT/URL) --> Chunking (Fixed / NLP / LLM Smart)
    --> Pinecone (upsert with integrated embedding)
```

Chunking modes (selectable per ingestion):
- **Fixed** -- recursive character splitting (1000 chars, 100 overlap)
- **NLP Dynamic** (default) -- sentence-boundary splitting, no LLM, ~50ms
- **LLM Smart** -- LLM analyzes full document and splits by topic boundaries (~60-100s)

**Chat Pipeline (Agentic RAG):**

```
User Question + History --> Agent Loop (max 3 iterations):
    --> LLM decides: call tool or answer
    --> Tool call? Execute (search / reformulate), add results, loop
    --> Answer? Stream tokens via SSE (chunk/sources/quality/done)
```

The agent has two tools:
- **search_knowledge_base** -- semantic similarity search against Pinecone with automatic **re-ranking** (over-fetches up to 20 candidates, re-ranks via Pinecone bge-reranker-v2-m3, returns top 8)
- **reformulate_query** -- LLM-powered query rewriting for better retrieval

After the answer streams, the system evaluates **faithfulness** (are claims supported by context?) and **context recall** (did the context contain enough info?) and returns quality scores. If scores are low (< 30%), a warning is displayed. If no relevant documents were found, quality evaluation is skipped entirely and the agent responds that it couldn't find relevant information.

The agent is instructed to **only answer from retrieved documents** -- it will not use its own knowledge to fill gaps.

### High-Level Diagram

```
+--------------------------------------------------------------+
|                     Frontend (Next.js SPA)                    |
|   Sidebar (KB panel, settings)  |  Chat Interface            |
+---------------+--------------------------------------+-------+
                | REST + SSE                           |
+---------------v--------------------------------------v-------+
|                    Backend API Server (.NET 9)                |
|  /ingest  |  /ingest/sources  |  /chat  |  /config  | /health|
+----+----------+----------+----------+---------+--------------+
     |          |          |                    |
  Loaders    Chunking    Agent Loop          LLM (GPT-4o-mini)
  (TXT/MD    (Fixed/     (function calling)  Reasoning + Streaming
   /URL)     NLP/Smart)   |         |        + Quality Eval
                          |         |
                     Pinecone    Query Rewrite
                     Search +    Service
                     Rerank      Tool
                     Tool
```

### Key Design Decisions

- **Agentic RAG** -- the LLM drives the retrieval loop via OpenAI function calling. Instead of a fixed retrieve-then-answer pipeline, the agent decides when to search, evaluates result quality, reformulates queries when needed, and answers only when it has sufficient context. Max 3 iterations per question.
- **Pinecone only** -- no vector store abstraction layer. Pinecone handles both storage and embedding via integrated llama-text-embed-v2, so there are no separate embedding API calls.
- **Hybrid chunking** -- three chunking modes selectable per ingestion: Fixed (fast, character-count), NLP Dynamic (sentence-boundary, default), and LLM Smart (topic-boundary, slow but highest quality).
- **Pinecone re-ranking** -- search results are automatically re-ranked using Pinecone's bge-reranker-v2-m3 model. The search tool over-fetches candidates and returns the top results after re-ranking for better relevance.
- **Configurable LLM provider** -- the main LLM (agent + answer generation + smart chunking + quality evaluation) and the rewrite LLM can be pointed to any OpenAI-compatible provider via environment variables (`LLM_BASE_URL`, `LLM_MODEL`, `LLM_API_KEY`).
- **SSE streaming** -- chat responses stream token-by-token using Server-Sent Events with four event types: `chunk`, `sources`, `quality`, and `done`.
- **Quality evaluation** -- after each answer, the system evaluates faithfulness (claims grounded in context) and context recall (context completeness) via parallel LLM calls. Scores are displayed under source citations. Low scores trigger a warning; evaluation is skipped when no documents were retrieved.
- **Knowledge-base only** -- the agent only answers from retrieved documents. If the knowledge base has no relevant information, the agent says so rather than using its own knowledge.
- **Agent decides everything** -- the agent handles both knowledge-base questions and conversational follow-ups (e.g. "summarise that"). No separate follow-up detection path.


## Features

- **Agentic RAG** -- LLM-driven retrieval loop with self-evaluation and query reformulation (up to 3 iterations)
- **Two agent tools**: knowledge base search (with auto re-ranking) and query reformulation
- **Hybrid chunking** -- choose per ingestion: Fixed (character-count), NLP Dynamic (sentence-boundary, default), or LLM Smart (topic-boundary)
- **Pinecone re-ranking** -- search results automatically re-ranked via bge-reranker-v2-m3 for better relevance
- **Quality evaluation** -- faithfulness and context recall scores displayed under each answer with color coding (green/yellow/red). Low-quality warning shown when scores fall below 30%. Skipped when no documents were retrieved.
- **Markdown rendering** -- bot responses rendered as formatted HTML (headings, bold, code blocks, lists)
- Document ingestion: Markdown (.md), plain text (.txt), and URLs
- Vector similarity search via Pinecone with integrated embeddings
- Configurable LLM provider -- swap OpenAI for any compatible provider via env vars
- Real-time SSE streaming responses with token-by-token display
- Source citations below each bot response (deduplicated across multiple search iterations)
- Knowledge base management: list ingested sources, clear all data
- Multi-turn conversation support with chat history sent per request
- Modern SaaS dashboard UI with dark sidebar, light content, and indigo accents
- Connection status indicator in the header
- Activity log with colour-coded entries (info, success, error)


## Prerequisites

Before you start, make sure you have:

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Node.js 18+](https://nodejs.org/) and npm
- An [OpenAI API key](https://platform.openai.com/api-keys) (used for answer generation and query rewriting)
- A [Pinecone API key](https://www.pinecone.io/) with an index configured for integrated embeddings (see Pinecone Configuration below)

### Pinecone Index Setup

Three Pinecone indexes are available (same configuration, different chunking strategies):

| Index | Host | Purpose |
|-------|------|---------|
| `rag-chatbot-v3` | `rag-chatbot-v3-y3gph8e.svc.aped-4627-b74a.pinecone.io` | Original fixed chunking data |
| `rag-chatbot-v3-smart` | `rag-chatbot-v3-smart-y3gph8e.svc.aped-4627-b74a.pinecone.io` | LLM smart chunking data |
| `rag-chatbot-v3-hybrid` | `rag-chatbot-v3-hybrid-y3gph8e.svc.aped-4627-b74a.pinecone.io` | Hybrid mode (all 3 chunking modes) |

All indexes share: `llama-text-embed-v2` (integrated), AWS us-east-1 (serverless), namespace `rag-chatbot`, text field `chunk_text`.

Switch between indexes by setting the `PINECONE_HOST` environment variable. If not set, defaults to the original index.


## Installation

1. **Clone the repository:**

   ```bash
   git clone <repository-url>
   cd rag-chatbot-v3
   ```

2. **Configure environment variables:**

   Copy the example file and fill in your API keys:

   ```bash
   cp .env.example .env
   ```

   At minimum, set these two values in `.env`:

   ```
   OPENAI_API_KEY=sk-your-openai-key
   PINECONE_API_KEY=your-pinecone-key
   ```

   The query rewrite variables (`REWRITE_LLM_*`) should also be set. See the Environment Variables section below for the full list.

3. **Backend setup:**

   ```bash
   cd backend
   dotnet restore
   dotnet build
   ```

4. **Frontend setup:**

   ```bash
   cd frontend
   npm install
   ```

5. **Frontend API URL (optional):**

   If the backend runs on a different host or port, create `frontend/.env.local`:

   ```
   NEXT_PUBLIC_API_URL=http://localhost:3010
   ```

   The default is `http://localhost:3010`.


## Running Development Servers

You need two terminals running simultaneously.

**Terminal 1 -- Backend:**

```bash
cd backend/RagChatbot.Api
dotnet run
```

The API server starts on **http://localhost:3010**. You can verify it is running:

```bash
curl http://localhost:3010/health
# {"status":"ok"}
```

**Terminal 2 -- Frontend:**

```bash
cd frontend
npm run dev
```

The frontend starts on **http://localhost:3000**.

Open [http://localhost:3000](http://localhost:3000) in your browser.


## Usage Guide

1. **Ingest documents** -- Use the Knowledge Base panel in the sidebar. Select a chunking mode (Fixed, NLP Dynamic, or LLM Smart) from the dropdown, then upload `.md` or `.txt` files, or paste a URL and click "Add URL". The activity log shows progress and any errors.

2. **List sources** -- Click "List Resources" in the KB panel to see all ingested documents.

3. **Ask questions** -- Type a question in the chat input and press Enter. The agent searches the knowledge base, evaluates retrieval quality, reformulates if needed, and streams a grounded answer with source citations.

4. **Follow-up conversation** -- Ask follow-up questions naturally. The agent uses conversation history to understand context and decides whether to search again or answer directly.

5. **Clear knowledge base** -- Click "Clear Knowledge Base" in the sidebar. A confirmation dialog appears. Clearing the KB also resets the chat history.


## API Endpoints

| Method | Endpoint | Purpose |
|--------|----------|---------|
| `POST` | `/ingest` | Ingest a file upload (multipart/form-data) or URL. Accepts optional `chunkingMode` field (`fixed`, `nlp`, `smart`; default: `nlp`) |
| `GET` | `/ingest/sources` | List unique ingested source names |
| `DELETE` | `/ingest/reset` | Clear all data from the knowledge base |
| `POST` | `/chat` | RAG query with SSE streaming response |
| `GET` | `/config` | Server configuration (model info, no secrets) |
| `GET` | `/health` | Health check (`{"status":"ok"}`) |

The `/chat` endpoint returns `Content-Type: text/event-stream` with four event types:
- `chunk` -- incremental answer text: `data: {"type":"chunk","text":"..."}`
- `sources` -- source documents: `data: {"type":"sources","sources":[...]}`
- `quality` -- answer quality scores: `data: {"type":"quality","faithfulness":0.92,"contextRecall":0.85,"warning":null}` (omitted when no search context)
- `done` -- stream complete: `data: {"type":"done"}`


## Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `OPENAI_API_KEY` | Yes | -- | OpenAI API key (default fallback for all LLM calls) |
| `PORT` | No | `3010` | Backend server listen port |
| `PINECONE_API_KEY` | Yes | -- | Pinecone API key |
| `PINECONE_HOST` | No | `rag-chatbot-v3-y3gph8e.svc...` | Pinecone index host — switch between standard, smart, and hybrid indexes |
| `LLM_BASE_URL` | No | `https://api.openai.com/v1` | Base URL for the main LLM (agent loop + answer generation + smart chunking) |
| `LLM_MODEL` | No | `gpt-4o-mini` | Model name for the main LLM |
| `LLM_API_KEY` | No | (uses OPENAI_API_KEY) | API key for the main LLM (allows different provider) |
| `REWRITE_LLM_MODEL` | No | `gpt-4o-mini` | Model name for query rewriting |
| `REWRITE_LLM_BASE_URL` | No | `https://api.openai.com/v1` | Base URL for the query rewrite LLM |
| `REWRITE_LLM_API_KEY` | No | (uses OPENAI_API_KEY) | API key for the rewrite LLM (if different from OpenAI key) |
| `CHUNK_SIZE` | No | `1000` | Fallback chunk size in characters (used when smart chunking fails) |
| `CHUNK_OVERLAP` | No | `100` | Fallback chunk overlap in characters |

Frontend environment (in `frontend/.env.local`):

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `NEXT_PUBLIC_API_URL` | No | `http://localhost:3010` | Backend API URL |


## Project Structure

```
rag-chatbot-v3/
|-- .env.example                         # Environment variable template
|-- CLAUDE.md                            # AI agent project context
|-- DECISIONS.md                         # Architectural decision records
|-- FEATURES.md                          # Feature checklist
|-- RAG-Chatbot-Functional-Spec.md       # Functional specification
|
|-- backend/
|   |-- RagChatbot.sln                   # Solution file
|   |-- RagChatbot.Api/                  # ASP.NET Core Web API
|   |   |-- Program.cs                   # App entry point, DI, middleware
|   |   |-- Controllers/
|   |   |   |-- ChatController.cs        # POST /chat (SSE streaming)
|   |   |   |-- IngestController.cs      # POST /ingest, GET /ingest/sources, DELETE /ingest/reset
|   |   |   |-- ConfigController.cs      # GET /config
|   |   |   |-- HealthController.cs      # GET /health
|   |-- RagChatbot.Core/                 # Domain layer (no dependencies)
|   |   |-- Configuration/AppConfig.cs
|   |   |-- Interfaces/                  # Service contracts
|   |   |-- Models/                      # Domain models (Document, ChatRequest, SseEvent, etc.)
|   |-- RagChatbot.Infrastructure/       # Implementation layer
|   |   |-- Chat/                        # AgenticRagPipelineService, LlmService
|   |   |   |-- Tools/                   # SearchKnowledgeBaseTool (with rerank), ReformulateQueryTool
|   |   |-- DocumentProcessing/          # NlpChunkingSplitter, SmartChunkingSplitter, RecursiveCharacterSplitter, TextFileLoader, WebPageLoader
|   |   |-- Ingestion/IngestionService.cs
|   |   |-- QueryRewrite/QueryRewriteService.cs
|   |   |-- VectorStore/PineconeService.cs
|   |-- RagChatbot.Tests/               # Unit and integration tests (213 tests)
|
|-- frontend/
|   |-- src/
|   |   |-- app/                         # Next.js App Router (layout.tsx, page.tsx)
|   |   |-- components/                  # React components
|   |   |   |-- Sidebar.tsx              # Sidebar container
|   |   |   |-- Header.tsx               # App header with status indicator
|   |   |   |-- ChatArea.tsx             # Chat container
|   |   |   |-- ChatInput.tsx            # Message input bar
|   |   |   |-- MessageList.tsx          # Scrollable message display
|   |   |   |-- BotMessage.tsx           # Bot message bubble
|   |   |   |-- UserMessage.tsx          # User message bubble
|   |   |   |-- SourceCitations.tsx      # Clickable source links
|   |   |   |-- KnowledgeBasePanel.tsx   # KB management (upload, URL, list, clear)
|   |   |   |-- FileUpload.tsx           # File upload component
|   |   |   |-- UrlIngest.tsx            # URL ingestion component
|   |   |   |-- ActivityLog.tsx          # Colour-coded activity log
|   |   |   |-- SettingsSection.tsx      # LLM config display
|   |   |   |-- ConfirmDialog.tsx        # Confirmation modal
|   |   |   |-- EmptyState.tsx           # Empty chat placeholder
|   |   |   |-- StatusIndicator.tsx      # Connection status dot
|   |   |   |-- ThinkingIndicator.tsx    # Animated thinking dots
|   |   |   |-- ResourceList.tsx         # Ingested sources list
|   |   |-- hooks/                       # Custom React hooks
|   |   |   |-- useChat.ts              # Chat state and SSE stream handling
|   |   |   |-- useConfig.ts            # Config fetching from /config
|   |   |   |-- useKnowledgeBase.ts      # KB operations (ingest, list, clear)
|   |   |-- services/                    # API client functions
|   |   |   |-- api.ts                   # REST API calls
|   |   |   |-- chatApi.ts              # SSE streaming client
|   |   |-- types/                       # TypeScript type definitions
|   |-- tailwind.config.ts
|   |-- tsconfig.json
|   |-- package.json
|
|-- documents/
|   |-- test-sample.md                   # Sample document for testing
|
|-- docs/
|   |-- ui-spec.md                       # UI design specification
```


## Running Tests

**Backend:**

```bash
cd backend
dotnet test
```

**Frontend:**

```bash
cd frontend

# Type checking
npx tsc --noEmit

# Linting
npm run lint

# Unit tests (if configured)
npm test
```


## Deployment

**Backend** -- Deployed to [Railway](https://railway.app/) using Docker. The `backend/Dockerfile` builds a multi-stage image with the .NET 9 SDK. Set all required environment variables in the Railway dashboard.

**Frontend** -- Deployed to [Vercel](https://vercel.com/). Set `NEXT_PUBLIC_API_URL` to the deployed backend URL in Vercel's environment settings.

**Live URLs:**
- Backend: `https://rag-chatbot-v3-production.up.railway.app`
- Frontend: `https://rag-chatbot-v3.vercel.app`

**Post-deployment checklist:**

- Set `NEXT_PUBLIC_API_URL` in Vercel to the Railway backend URL
- Set `OPENAI_API_KEY`, `PINECONE_API_KEY`, `PORT`, `REWRITE_LLM_MODEL` in Railway environment variables
- Set `PINECONE_HOST` to select the desired index (standard, smart, or hybrid)
- Optionally set `LLM_BASE_URL`, `LLM_MODEL`, `LLM_API_KEY` to use a different LLM provider
- Update CORS on the backend to allow the Vercel domain (currently allows any origin)
- Verify the `/health` endpoint responds on the deployed backend


## License

MIT
