# RAG Chatbot v3

A full-stack **Agentic RAG** chatbot that ingests documents, stores them as vector embeddings in Pinecone, and uses an LLM-driven agent loop to provide grounded, cited answers via OpenAI GPT-4o-mini with real-time streaming. Built with a C#/.NET 9 backend (Clean Architecture) and a Next.js 14 frontend styled as a modern SaaS dashboard.

Unlike traditional RAG (single-pass retrieve-then-answer), **Agentic RAG** gives the LLM ownership of the retrieval loop. The agent decides when to search, evaluates whether results are sufficient, reformulates queries when needed, and only answers when it has enough context -- up to 3 retrieval iterations per question.


## Screenshots

Screenshots of the running application are available in the `playwright_screenshots/` directory.


## Tech Stack

| Layer | Technology |
|-------|------------|
| Backend | C# / .NET 9 / ASP.NET Core Web API / Clean Architecture |
| Frontend | Next.js 14 / TypeScript / Tailwind CSS |
| Vector Store | Pinecone (serverless, AWS us-east-1, integrated llama-text-embed-v2 embeddings) |
| LLM | OpenAI GPT-4o-mini (agentic reasoning, answer generation, query rewriting) |
| Agent Framework | Custom agentic loop with OpenAI function calling (tool_use) |
| Design | Modern SaaS dashboard: dark sidebar, light content area, indigo accent |


## Architecture

The backend follows **Clean Architecture** with three projects:

- **RagChatbot.Api** -- ASP.NET Core controllers, middleware, program entry point
- **RagChatbot.Core** -- domain models, interfaces, configuration (no external dependencies)
- **RagChatbot.Infrastructure** -- implementations: Pinecone client, LLM service, document processing, query rewrite

### Core Pipelines

**Ingestion Pipeline:**

```
Document --> Loader (MD/TXT/URL) --> Chunking --> Pinecone (upsert with integrated embedding)
```

**Chat Pipeline (Agentic RAG):**

```
User Question + History --> Agent Loop (max 3 iterations):
    --> LLM decides: call tool or answer
    --> Tool call? Execute (search / reformulate), add results, loop
    --> Answer? Stream tokens via SSE (chunk/sources/done)
```

The agent has two tools:
- **search_knowledge_base** -- semantic similarity search against Pinecone (with similarity scores)
- **reformulate_query** -- LLM-powered query rewriting for better retrieval

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
  Loaders    Splitters   Agent Loop          LLM (GPT-4o-mini)
  (TXT/MD    (Markdown-  (function calling)  Reasoning + Streaming
   /URL)      aware +     |         |
              recursive)  |         |
                     Pinecone    Query Rewrite
                     Search      Service
                     Tool        Tool
```

### Key Design Decisions

- **Agentic RAG** -- the LLM drives the retrieval loop via OpenAI function calling. Instead of a fixed retrieve-then-answer pipeline, the agent decides when to search, evaluates result quality, reformulates queries when needed, and answers only when it has sufficient context. Max 3 iterations per question.
- **Pinecone only** -- no vector store abstraction layer. Pinecone handles both storage and embedding via integrated llama-text-embed-v2, so there are no separate embedding API calls.
- **Configurable LLM provider** -- the main LLM (agent + answer generation) and the rewrite LLM can be pointed to any OpenAI-compatible provider via environment variables (`LLM_BASE_URL`, `LLM_MODEL`, `LLM_API_KEY`).
- **SSE streaming** -- chat responses stream token-by-token using Server-Sent Events with three event types: `chunk`, `sources`, and `done`.
- **Agent decides everything** -- the agent handles both knowledge-base questions and conversational follow-ups (e.g. "summarise that"). No separate follow-up detection path.


## Features

- **Agentic RAG** -- LLM-driven retrieval loop with self-evaluation and query reformulation (up to 3 iterations)
- **Two agent tools**: knowledge base search (with similarity scores) and query reformulation
- Document ingestion: Markdown (.md), plain text (.txt), and URLs
- Markdown-aware chunking that splits by heading boundaries for .md files
- Recursive character splitting for TXT and URL content (configurable chunk size and overlap)
- Vector similarity search via Pinecone with integrated embeddings and similarity scores
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

Your Pinecone index must be set up with these settings:

| Setting | Value |
|---------|-------|
| Index name | `rag-chatbot-v3` |
| Namespace | `rag-chatbot` |
| Embedding model | `llama-text-embed-v2` (integrated) |
| Cloud / Region | AWS us-east-1 (serverless) |
| Text field for embedding | `chunk_text` |


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

1. **Ingest documents** -- Use the Knowledge Base panel in the sidebar. Upload `.md` or `.txt` files, or paste a URL and click "Add URL". The activity log shows progress and any errors.

2. **List sources** -- Click "List Resources" in the KB panel to see all ingested documents.

3. **Ask questions** -- Type a question in the chat input and press Enter. The agent searches the knowledge base, evaluates retrieval quality, reformulates if needed, and streams a grounded answer with source citations.

4. **Follow-up conversation** -- Ask follow-up questions naturally. The agent uses conversation history to understand context and decides whether to search again or answer directly.

5. **Clear knowledge base** -- Click "Clear Knowledge Base" in the sidebar. A confirmation dialog appears. Clearing the KB also resets the chat history.


## API Endpoints

| Method | Endpoint | Purpose |
|--------|----------|---------|
| `POST` | `/ingest` | Ingest a file upload (multipart/form-data) or URL |
| `GET` | `/ingest/sources` | List unique ingested source names |
| `DELETE` | `/ingest/reset` | Clear all data from the knowledge base |
| `POST` | `/chat` | RAG query with SSE streaming response |
| `GET` | `/config` | Server configuration (model info, no secrets) |
| `GET` | `/health` | Health check (`{"status":"ok"}`) |

The `/chat` endpoint returns `Content-Type: text/event-stream` with three event types:
- `chunk` -- incremental answer text: `data: {"type":"chunk","text":"..."}`
- `sources` -- source documents: `data: {"type":"sources","sources":[...]}`
- `done` -- stream complete: `data: {"type":"done"}`


## Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `OPENAI_API_KEY` | Yes | -- | OpenAI API key (default fallback for all LLM calls) |
| `PORT` | No | `3010` | Backend server listen port |
| `PINECONE_API_KEY` | Yes | -- | Pinecone API key |
| `LLM_BASE_URL` | No | `https://api.openai.com/v1` | Base URL for the main LLM (agent loop + answer generation) |
| `LLM_MODEL` | No | `gpt-4o-mini` | Model name for the main LLM |
| `LLM_API_KEY` | No | (uses OPENAI_API_KEY) | API key for the main LLM (allows different provider) |
| `REWRITE_LLM_MODEL` | No | `gpt-4o-mini` | Model name for query rewriting |
| `REWRITE_LLM_BASE_URL` | No | `https://api.openai.com/v1` | Base URL for the query rewrite LLM |
| `REWRITE_LLM_API_KEY` | No | (uses OPENAI_API_KEY) | API key for the rewrite LLM (if different from OpenAI key) |
| `CHUNK_SIZE` | No | `1000` | Document chunk size in characters |
| `CHUNK_OVERLAP` | No | `100` | Overlap between chunks in characters |

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
|   |   |   |-- Tools/                   # SearchKnowledgeBaseTool, ReformulateQueryTool
|   |   |-- DocumentProcessing/          # TextFileLoader, WebPageLoader, MarkdownSplitter, RecursiveCharacterSplitter
|   |   |-- Ingestion/IngestionService.cs
|   |   |-- QueryRewrite/QueryRewriteService.cs
|   |   |-- VectorStore/PineconeService.cs
|   |-- RagChatbot.Tests/               # Unit and integration tests (163 tests)
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
- Optionally set `LLM_BASE_URL`, `LLM_MODEL`, `LLM_API_KEY` to use a different LLM provider
- Update CORS on the backend to allow the Vercel domain (currently allows any origin)
- Verify the `/health` endpoint responds on the deployed backend


## License

MIT
