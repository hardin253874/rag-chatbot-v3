# Sprint B5 — RAG Pipeline + SSE Streaming

## Goal
Implement the core RAG pipeline and `POST /chat` endpoint with SSE streaming. This is the final backend sprint.

## Features

### 1. Conversational Detection Service
- `IConversationalDetector` interface in Core
- `ConversationalDetector` implementation in Infrastructure
- Case-insensitive phrase matching: "you just said", "you mentioned", "summarise", "summarize", "what did you", "previous", "last answer", "above", "repeat"
- Returns `true` if ANY phrase found in the question

### 2. LLM Streaming Service
- `ILlmService` interface in Core
- `LlmService` implementation in Infrastructure
- Calls OpenAI `POST /v1/chat/completions` with `stream: true`
- Model: `gpt-4o-mini`, temperature configurable (default 0.2)
- Reads SSE response line by line, parses `data:` lines
- Extracts `choices[0].delta.content`, skips `[DONE]` marker
- Returns `IAsyncEnumerable<string>` of content tokens

### 3. RAG Pipeline Service
- `IRagPipelineService` interface in Core
- `RagPipelineService` implementation in Infrastructure
- Orchestrates: conversational check -> rewrite -> search -> context assembly -> LLM streaming
- Pipeline logic:
  1. If conversational follow-up AND history exists: skip vector search, use conversation-only prompt
  2. Rewrite query via `IQueryRewriteService`
  3. Search Pinecone via `IPineconeService.SimilaritySearchAsync` (top 5)
  4. If no results: yield single chunk "I couldn't find any relevant information in the knowledge base.", then done
  5. Build prompt with numbered context `[1]`, `[2]`, etc.
  6. Stream LLM response as `chunk` events
  7. Yield `sources` event (unique sources from retrieved docs)
  8. Yield `done` event
- Rewritten query used for search; original question used in LLM prompt (ADR-003)

### 4. Chat Controller + SSE Endpoint
- `POST /chat` accepts `{ question, history }` JSON body
- Returns `Content-Type: text/event-stream` with `Cache-Control: no-cache`, `Connection: keep-alive`
- Streams `data: {"type":"chunk","text":"..."}` events
- Streams `data: {"type":"sources","sources":[...]}` event
- Streams `data: {"type":"done"}` event
- Returns HTTP 400 if `question` is missing/empty

### 5. New Models
- `ChatMessage` (Role, Content)
- `ChatRequest` (Question, History)
- `SseEvent` (Type, Text, Sources)

## Definition of Done
- [x] `POST /chat` with `{"question":"...","history":[]}` returns `Content-Type: text/event-stream` HTTP 200
- [x] SSE stream contains `data: {"type":"chunk","text":"..."}` events
- [x] SSE stream contains `data: {"type":"sources","sources":[...]}` event
- [x] SSE stream ends with `data: {"type":"done"}`
- [x] `POST /chat` with no `question` returns HTTP 400
- [x] Conversational follow-ups with non-empty history skip vector search
- [x] Non-conversational questions go through: rewrite -> Pinecone search -> context assembly -> LLM generation
- [x] Rewritten query used for vector search; original question used in LLM prompt
- [x] Context chunks numbered `[1]`, `[2]`, etc.
- [x] LLM: model `gpt-4o-mini`, temperature `0.2`, streaming enabled
- [x] `dotnet build` = 0 errors
- [x] `dotnet test` = all pass

## Files to Create
- `backend/RagChatbot.Core/Models/ChatMessage.cs`
- `backend/RagChatbot.Core/Models/ChatRequest.cs`
- `backend/RagChatbot.Core/Models/SseEvent.cs`
- `backend/RagChatbot.Core/Interfaces/IConversationalDetector.cs`
- `backend/RagChatbot.Core/Interfaces/ILlmService.cs`
- `backend/RagChatbot.Core/Interfaces/IRagPipelineService.cs`
- `backend/RagChatbot.Infrastructure/Chat/ConversationalDetector.cs`
- `backend/RagChatbot.Infrastructure/Chat/LlmService.cs`
- `backend/RagChatbot.Infrastructure/Chat/RagPipelineService.cs`
- `backend/RagChatbot.Api/Controllers/ChatController.cs`

## Files to Modify
- `backend/RagChatbot.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs` (register new services)

## Test Files to Create
- `backend/RagChatbot.Tests/Chat/ConversationalDetectorTests.cs`
- `backend/RagChatbot.Tests/Chat/LlmServiceTests.cs`
- `backend/RagChatbot.Tests/Chat/RagPipelineServiceTests.cs`
- `backend/RagChatbot.Tests/ChatControllerTests.cs`

## Test Plan (TDD Order)
1. ConversationalDetector — phrase matching (positive, negative, case-insensitive, empty)
2. LlmService — SSE parsing, token extraction, error handling, authorization header
3. RagPipelineService — full pipeline (RAG path), conversational path, no-results path, sources extraction
4. ChatController — 400 on missing question, 200 with SSE content type, event stream format
