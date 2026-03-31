# RAG Chatbot v3

A full-stack Retrieval-Augmented Generation (RAG) chatbot that ingests documents, stores them as vector embeddings in Pinecone, and uses similarity search to provide grounded, cited answers via OpenAI GPT-4o-mini with real-time streaming. Built with a C#/.NET 9 backend (Clean Architecture) and a Next.js 14 frontend styled as a modern SaaS dashboard.

Users upload documents or paste URLs, the system chunks and indexes the content, and then answers natural language questions by retrieving relevant chunks and streaming LLM-generated responses with source citations.


## Screenshots

Screenshots of the running application are available in the `playwright_screenshots/` directory.


## Tech Stack

| Layer | Technology |
|-------|------------|
| Backend | C# / .NET 9 / ASP.NET Core Web API / Clean Architecture |
| Frontend | Next.js 14 / TypeScript / Tailwind CSS |
| Vector Store | Pinecone (serverless, AWS us-east-1, integrated llama-text-embed-v2 embeddings) |
| LLM | OpenAI GPT-4o-mini (answer generation + query rewriting) |
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

**Chat Pipeline:**

```
User Question --> Query Rewrite (LLM) --> Pinecone Similarity Search (top 5)
    --> Context Assembly --> LLM Streaming --> SSE Response (chunk/sources/done)
```

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
+----+----------+----------+----------+----------+-------------+
     |          |          |          |          |
  Loaders    Splitters   Pinecone   Query     LLM (GPT-4o-mini)
  (TXT/MD    (Markdown-  Service   Rewrite    Answer Generation
   /URL)      aware +              Service    + Streaming
              recursive)
```

### Key Design Decisions

- **Pinecone only** -- no vector store abstraction layer. Pinecone handles both storage and embedding via integrated llama-text-embed-v2, so there are no separate embedding API calls.
- **Query rewrite always on** -- every user query is rewritten by the LLM before vector search to improve retrieval quality. The original question is still used in the LLM prompt. Falls back silently to the raw query on any failure.
- **SSE streaming** -- chat responses stream token-by-token using Server-Sent Events with three event types: `chunk`, `sources`, and `done`.
- **Conversational follow-up detection** -- recognises when a user is referring to the prior conversation (e.g. "summarise what you said") and skips vector search, answering from chat history instead.


## Features

- Document ingestion: Markdown (.md), plain text (.txt), and URLs
- Markdown-aware chunking that splits by heading boundaries for .md files
- Recursive character splitting for TXT and URL content (configurable chunk size and overlap)
- Vector similarity search via Pinecone with integrated embeddings
- LLM-powered query rewriting for better retrieval (always on, graceful degradation)
- Conversational follow-up detection (skips retrieval for conversational questions)
- Real-time SSE streaming responses with token-by-token display
- Source citations below each bot response
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

3. **Ask questions** -- Type a question in the chat input and press Enter. The system rewrites your query for better search results, retrieves relevant chunks from Pinecone, and streams a grounded answer with source citations.

4. **Follow-up conversation** -- Ask follow-up questions naturally. The system detects conversational follow-ups (like "summarise that" or "what did you just say") and answers from chat history without re-querying the vector store.

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
| `OPENAI_API_KEY` | Yes | -- | OpenAI API key for answer generation and query rewriting |
| `PORT` | No | `3010` | Backend server listen port |
| `PINECONE_API_KEY` | Yes | -- | Pinecone API key |
| `REWRITE_LLM_BASE_URL` | No | `https://api.openai.com/v1` | Base URL for the query rewrite LLM |
| `REWRITE_LLM_MODEL` | No | `gpt-4o-mini` | Model name for query rewriting |
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
|   |   |-- Chat/                        # LlmService, RagPipelineService, ConversationalDetector
|   |   |-- DocumentProcessing/          # TextFileLoader, WebPageLoader, MarkdownSplitter, RecursiveCharacterSplitter
|   |   |-- Ingestion/IngestionService.cs
|   |   |-- QueryRewrite/QueryRewriteService.cs
|   |   |-- VectorStore/PineconeService.cs
|   |-- RagChatbot.Tests/               # Unit and integration tests
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

**Backend** -- Deploy to any cloud platform that supports .NET 9: Azure App Service, AWS Elastic Beanstalk, Railway, Render, or a Linux VPS with the .NET runtime. Set all required environment variables on the host.

**Frontend** -- Deploy to [Vercel](https://vercel.com/) (built for Next.js). Set `NEXT_PUBLIC_API_URL` to the deployed backend URL in Vercel's environment settings.

**Post-deployment checklist:**

- Set `NEXT_PUBLIC_API_URL` in the frontend deployment to point to the backend URL
- Update CORS on the backend to allow the Vercel domain (currently allows any origin for development)
- Verify the `/health` endpoint responds on the deployed backend
- Confirm Pinecone connectivity from the deployed environment


## License

MIT
