# RAG Chatbot v3 — Architectural Decisions

Log of architectural decisions. All agents must follow these when implementing.

---

## ADR-001: Pinecone Only — No Vector Store Abstraction

**Decision:** Use Pinecone as the sole vector store. No `IVectorStore` interface, no factory pattern, no ChromaDB.

**Rationale:** Project will deploy to cloud. A single implementation reduces complexity. If a second store is needed later, an interface can be extracted at that time.

**Index config:** `rag-chatbot-v3`, namespace `rag-chatbot`, integrated `llama-text-embed-v2` embeddings, AWS us-east-1 serverless.

---

## ADR-002: SSE Streaming for POST /chat

**Decision:** The `/chat` endpoint streams responses using Server-Sent Events (SSE) with `Content-Type: text/event-stream`.

**Event types:**
- `chunk` — `{"type":"chunk","text":"..."}` — incremental answer text
- `sources` — `{"type":"sources","sources":["file.pdf","https://..."]}` — source documents
- `done` — `{"type":"done"}` — stream complete

**Rationale:** SSE provides real-time token-by-token streaming with minimal client complexity. Each event is a `data: ` line followed by JSON.

---

## ADR-003: Query Rewrite Always On

**Decision:** Every user query is rewritten by the LLM before vector search. No raw mode, no toggle. The rewritten query is used ONLY for vector search. The original user question is ALWAYS used in the LLM prompt.

**Graceful degradation:** If rewrite fails (config missing, API error, empty response), fall back silently to the original query.

**Rationale:** Simplifies the API (no `queryMode` parameter) and frontend (no dropdown). Rewrite consistently improves retrieval quality.

---

## ADR-004: API Keys Never Exposed

**Decision:** `GET /config` returns rewrite LLM model name and base URL but NEVER exposes API keys. All keys stay server-side in environment variables.

---

## ADR-005: Context Reset Harness

**Decision:** Each agent session reads `claude-progress.txt`, `FEATURES.md`, and `DECISIONS.md` before acting. After completing work, agents append to `claude-progress.txt` and update `FEATURES.md`.

**Rationale:** Prevents context degradation in long-running agent sessions. Based on Anthropic's harness design pattern.

---

## ADR-006: Generator-Evaluator Separation

**Decision:** Developer agents (generators) write code. A separate Evaluator agent grades the output using Playwright against the running app. The evaluator does NOT fix code.

**Rationale:** Self-evaluation is unreliable. Separating generation from evaluation enables skeptical QA.

---

## ADR-007: Sprint Contracts

**Decision:** Before each sprint, the Developer agent writes a `SPRINT_CONTRACT_[N].md` with specific features, Definition of Done, and file lists. The orchestrator must approve before coding begins.

---

## ADR-008: Tech Stack

- **Backend:** C# / .NET 8 / ASP.NET Core Web API
- **Backend architecture:** Clean Architecture — Api, Core, Infrastructure projects
- **Frontend:** Next.js 14 / TypeScript / Tailwind CSS
- **Frontend style:** Modern SaaS dashboard (Designer to specify)
- **Vector store:** Pinecone (serverless, AWS us-east-1)
- **Embeddings:** Pinecone integrated `llama-text-embed-v2` (no OpenAI embedding calls)
- **LLM:** OpenAI GPT-4o-mini (answers + query rewrite)
- **Deployment:** Backend — free cloud server (TBD), Frontend — Vercel

---

## ADR-009: Markdown-Aware Chunking

**Decision:** Use markdown-aware splitting for `.md` files (split by heading boundaries). Use recursive character splitting for TXT and URL content. Chunk size and overlap configurable via `CHUNK_SIZE` (default 1000) and `CHUNK_OVERLAP` (default 100) env vars.

**Rationale:** Preserves document structure for markdown files while keeping flexibility for other formats.

---

## ADR-010: MVP File Types

**Decision:** Support MD, TXT, and URL ingestion only. PDF and DOCX deferred to a later phase.

**Rationale:** Reduces initial scope. PDF parsing requires a library dependency. Can be added incrementally.

---

## ADR-011: No Docker

**Decision:** No Docker or docker-compose. Local dev runs via `dotnet run` (backend) and `npm run dev` (frontend).

**Rationale:** Project deploys to separate cloud services. Docker adds complexity without benefit for this workflow.

---

## ADR-012: CORS Allow Any Origin

**Decision:** Backend CORS allows any origin for now. Will be restricted to the Vercel domain when deployed.

---

## ADR-013: 3-Strike Escalation Rule

**Decision:** If any agent hits a problem and 3 attempts fail to resolve it, the agent must STOP and escalate to the Coordinator. If the Coordinator also cannot resolve after 3 attempts, escalate to the user for brainstorming.

**Escalation chain:** Agent → Coordinator → User
