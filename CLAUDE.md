# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

RAG (Retrieval-Augmented Generation) Chatbot v3 — a full-stack application that ingests documents, stores them as vector embeddings, and uses similarity search to provide grounded, cited answers via LLM streaming.

- **Backend:** C# / .NET 8 / ASP.NET Core Web API
- **Frontend:** Next.js 14 / React / TypeScript / Tailwind CSS
- **Vector Stores:** Dual-backend — ChromaDB (local) and Pinecone (cloud)
- **LLM:** OpenAI GPT-4o-mini for answer generation; optional LLM-powered query rewriting
- **Functional Spec:** `RAG-Chatbot-Functional-Spec.md` (project root)

### Core Pipelines

**Ingestion:** Document -> Loader (PDF/TXT/MD/URL) -> Chunking (1000 chars, 100 overlap) -> Vector Store

**Chat:** Question -> [Query Rewrite] -> Vector Search (top 5) -> Context Assembly -> LLM Streaming -> SSE (chunk/sources/done)

### API Endpoints

| Method | Endpoint | Purpose |
|--------|----------|---------|
| POST | `/ingest` | Ingest file upload or URL |
| GET | `/ingest/sources` | List ingested sources |
| DELETE | `/ingest/reset` | Clear knowledge base |
| POST | `/chat` | RAG query with SSE streaming response |
| GET | `/config` | Server config (no secrets) |
| GET | `/health` | Health check |

All endpoints accept optional `vectorStore` (`local`/`cloud`) and `queryMode` (`raw`/`rewrite`) overrides.

### Pinecone Configuration

| Setting | Value |
|---------|-------|
| Index | `rag-chatbot-v3` |
| Namespace | `rag-chatbot` |
| Embedding | `llama-text-embed-v2` (integrated) |
| Region | AWS us-east-1 (serverless) |
| Text field | `chunk_text` |
| Batch size | 96 records per upsert |

## Agent Team & Harness

This project uses a **generator-evaluator harness** (based on [Anthropic's harness design pattern](https://www.anthropic.com/engineering/harness-design-long-running-apps)) with 4 specialist agents + 1 evaluator:

| Agent | File | Role |
|-------|------|------|
| **PM** | (orchestrator-managed) | Sprint planning, feature breakdown, priorities |
| **Designer** | (orchestrator-managed) | UI/UX spec, component design, Tailwind system |
| **Backend Developer** | `AgentTeam/backend_developer.md` | C#/.NET API server (generator) |
| **Frontend Developer** | `AgentTeam/frontend_developer.md` | Next.js frontend (generator) |
| **Evaluator** | `AgentTeam/evaluator.md` | QA via Playwright against running app (grader) |

The user talks to the **orchestrator** (coordinator), who dispatches work to agents.

### Execution Flow

See `AgentTeam/PLAYBOOK.md` for the full pipeline:
1. **Phase 0** — Harness setup (done)
2. **Phase 0.5** — Brainstorm with user (clarify requirements, scope, skill assignments)
3. **Phase 1** — PM produces sprint plan
4. **Phase 2** — Designer produces `docs/ui-spec.md`
5. **Phase 3+** — Developer sprints with generator-evaluator loop

### Sprint Cycle (generator-evaluator loop)

```
Developer writes SPRINT_CONTRACT_[N].md → Orchestrator approves
  → Developer implements (generator) → Developer self-evaluates
  → Evaluator grades via Playwright → writes SPRINT_EVALUATION_[N].md
  → PASS: update FEATURES.md, progress, next sprint
  → FAIL: Developer re-runs with evaluation as input (max 2 retries)
```

### Shared State Files

| File | Purpose |
|------|---------|
| `claude-progress.txt` | Append-only session log — all agents read/write |
| `FEATURES.md` | Canonical feature checklist with checkboxes |
| `DECISIONS.md` | Architectural decisions — all agents must follow |
| `BLOCKERS.md` | Cross-agent blockers (Open / Resolved / Dependency Map) |

## Development Skills (Superpowers)

Located in `.claude/skills/superpowers/`, these skills guide how agents work:

### Discovery
| Skill | Used by | Purpose |
|-------|---------|---------|
| `brainstorming` | Coordinator | Explore requirements with user before implementation — clarify scope, constraints, skill assignments |

### Planning & Execution
| Skill | Used by | Purpose |
|-------|---------|---------|
| `writing-plans` | PM | Write detailed, bite-sized implementation plans with TDD steps |
| `executing-plans` | Backend/Frontend Dev | Execute plans task-by-task with review checkpoints |
| `subagent-driven-development` | Coordinator | Dispatch fresh subagent per task, two-stage review (spec + quality) |
| `dispatching-parallel-agents` | Coordinator | Run independent tasks in parallel (e.g., backend + frontend) |

### Quality & Discipline
| Skill | Used by | Purpose |
|-------|---------|---------|
| `test-driven-development` | Backend/Frontend Dev | Red-green-refactor — no production code without failing test first |
| `verification-before-completion` | All agents | Evidence before claims — run verification, then report |
| `systematic-debugging` | Backend/Frontend Dev | Root cause first, no random fixes — 4-phase investigation |

### Code Review
| Skill | Used by | Purpose |
|-------|---------|---------|
| `requesting-code-review` | Coordinator | Dispatch review subagent after task completion |
| `receiving-code-review` | Backend/Frontend Dev | Handle review feedback — verify, evaluate, then implement |

## Key Commands

```bash
# Backend (C#/.NET)
dotnet build
dotnet run
dotnet test

# Frontend (Next.js)
npm run dev
npm run build
npm run test
npm run lint
npx tsc --noEmit
```

## Available Skills & Slash Commands

| Command | Skill | Purpose |
|---------|-------|---------|
| `/browser` | agent-browser | Browser automation CLI — navigate, snapshot, interact, screenshot |
| `/playwright` | playwright-skill | Playwright browser automation — test scripts to temp dir, execute via `run.js` |
| `/frontend-patterns` | frontend-patterns | React/Next.js patterns — components, hooks, state, performance, accessibility |

### agent-browser (`/browser`)

CLI for AI-driven browser automation. Core workflow: `open` -> `snapshot -i` (get `@e1` refs) -> interact -> re-snapshot after DOM changes. Full command reference in `.claude/skills/agent-browser/references/commands.md`.

### Playwright (`/playwright`)

Writes scripts to `C:\Users\d\AppData\Local\Temp\playwright-test-*.js`, executes via:
```bash
cd .claude/skills/playwright-skill && node run.js "<script-path>"
```

- Auto-detect dev servers first via `detectDevServers()`
- `headless: false` by default
- Screenshots to `C:\Developments\Develop\MyProjects\working\rag-chatbot-v3\playwright_screenshots\`
- First-time setup: `cd .claude/skills/playwright-skill && npm run setup`

### Frontend Patterns (`/frontend-patterns`)

React/TypeScript reference patterns: composition, compound components, render props, custom hooks, Context+Reducer, memoization, code splitting, virtualization, forms, error boundaries, Framer Motion, accessibility.

## Integrations

- **Pinecone MCP** — configured in `.mcp.json`, provides tools for index management and search
- **Pinecone plugin** (`pinecone@claude-plugins-official`) — skills and commands for Pinecone workflows
- **Pinecone quickstart** — available via `Skill(pinecone:quickstart)`

## Git Policy

**NEVER run `git commit`** — only the user commits manually. You may run `git diff`, `git status`, `git add`, and suggest commit messages.
