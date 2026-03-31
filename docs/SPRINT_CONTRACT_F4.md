# Sprint F4 Contract -- SSE Handling + Chat History + Integration

**Sprint:** F4 (Final Frontend Sprint)
**Agent:** Frontend Developer
**Date:** 2026-03-31
**Status:** Complete

---

## Goal

Replace the mock chat implementation with real SSE streaming against the live backend `POST /chat` endpoint. Implement chat history for multi-turn conversation.

---

## Deliverables

### 1. SSE Stream Types (`src/types/chat.ts`)
- Add `SseEvent` interface: `{ type: 'chunk' | 'sources' | 'done'; text?: string; sources?: string[] }`
- Add `ChatHistoryEntry` interface: `{ role: 'user' | 'assistant'; content: string }` for the request payload
- Existing `ChatMessage` unchanged

### 2. Chat API Service (`src/services/chatApi.ts`)
- New file with `streamChat()` async generator function
- POSTs to `${API_URL}/chat` with `{ question, history }` JSON body
- Reads response as `ReadableStream` via `response.body.getReader()`
- Parses `data: ` prefixed lines, yields parsed `SseEvent` objects
- Handles buffer splitting for partial lines
- Throws on non-OK response

### 3. Replace `useChat` Hook (`src/hooks/useChat.ts`)
- Remove all mock data and mock responses
- Maintain `messages: ChatMessage[]` state (user + assistant messages)
- Maintain `isStreaming: boolean` state
- `sendMessage(text)`:
  - Add user message to messages
  - Set isStreaming = true
  - Add placeholder bot message (empty content)
  - Build history from completed messages (before current question)
  - POST to /chat with question + history
  - On `chunk` event: append text to last bot message
  - On `sources` event: set sources on last bot message
  - On `done` event: set isStreaming = false
  - On error: set bot message content to error text, set isStreaming = false
- `clearMessages()`: reset messages to empty array (unchanged behavior)
- Support `AbortController` for potential future cancellation

### 4. Update `MessageList` Rendering Logic (`src/components/MessageList.tsx`)
- Show `ThinkingIndicator` only when streaming AND the last bot message has empty content
- Once first chunk arrives (bot message has content), hide thinking indicator -- the streaming text IS the visual feedback
- This avoids showing both thinking dots and streaming text simultaneously

### 5. Update `BotMessage` Component (`src/components/BotMessage.tsx`)
- Handle empty content gracefully (placeholder bot message before first chunk)
- No other changes needed -- component already renders content and sources

---

## Files Changed

| File | Action |
|------|--------|
| `src/types/chat.ts` | Modified -- add SseEvent, ChatHistoryEntry |
| `src/services/chatApi.ts` | New -- SSE streaming function |
| `src/hooks/useChat.ts` | Rewritten -- real SSE instead of mock |
| `src/components/MessageList.tsx` | Modified -- thinking indicator logic |
| `src/components/BotMessage.tsx` | Modified -- handle empty content |

---

## What is NOT Changing

- `ChatArea.tsx` -- no changes needed, receives same props
- `ChatInput.tsx` -- no changes needed
- `UserMessage.tsx` -- no changes needed
- `page.tsx` -- no changes needed, same useChat API
- `api.ts` -- no changes needed, ingestion API separate
- `Sidebar`, `Header`, `KnowledgeBasePanel` -- no changes

---

## Definition of Done

- [x] Sending a question POSTs to `${API_URL}/chat` with `{"question":"...","history":[...]}`
- [x] Response read as ReadableStream, buffered, split by newlines
- [x] `chunk` events append text to bot message bubble in real-time
- [x] `sources` events render clickable source links below bot message
- [x] `done` event re-enables input, removes thinking indicator
- [x] After stream ends, messages array contains user question + full bot answer
- [x] History sent with subsequent /chat requests for multi-turn conversation
- [x] History cleared on page refresh (useState, not persisted)
- [x] KB reset still clears chat history (verify existing integration)
- [x] On stream error: thinking indicator removed, error shown as bot message
- [x] `npx tsc --noEmit` = 0 errors
- [x] `npm run lint` = 0 errors
- [x] No `any` types
- [x] No `console.log` in production paths
- [x] ARIA attributes maintained on all interactive elements
