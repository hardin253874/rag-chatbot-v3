# Sprint Contract F3 -- Chat Interface (UI Only)

**Agent:** Frontend Developer
**Date:** 2026-03-31
**Status:** Complete

---

## What Will Be Built

1. **Chat types** -- `ChatMessage` interface with id, role, content, sources
2. **MessageList** -- scrollable container, renders messages or EmptyState, auto-scrolls on new messages
3. **UserMessage** -- right-aligned indigo bubble with message-enter animation
4. **BotMessage** -- left-aligned white bubble with shadow, optional source citations
5. **SourceCitations** -- clickable links below bot messages (URLs open new tab)
6. **ThinkingIndicator** -- 3 animated bouncing dots using thinking-bounce keyframe
7. **ChatInput** -- enhanced from F1 placeholder: real text input + send button, Enter key support, disabled state during streaming
8. **useChat hook** -- manages messages state, sendMessage(text), isStreaming. Mock mode: adds user message, shows thinking for 1s, adds mock bot response with sample sources
9. **ChatArea** -- wired up to useChat, renders MessageList or EmptyState, shows ThinkingIndicator during streaming

---

## Definition of Done

- [ ] Empty state shows icon + "Ask a question about your documents" when no messages
- [ ] User messages render right-aligned in indigo bubble (`bg-indigo-50`, `text-slate-900`)
- [ ] Bot messages render left-aligned in white bubble with shadow
- [ ] Source citations render as clickable links below bot messages (URLs open new tab)
- [ ] Thinking indicator shows 3 animated bouncing dots
- [ ] Text input + send button at bottom, full width
- [ ] Enter key triggers send (same as click)
- [ ] Empty messages prevented (send button disabled when input empty)
- [ ] Input and send button visually disabled during "streaming" state
- [ ] After "response complete", input re-enables and gets focus
- [ ] Messages area auto-scrolls when new messages added
- [ ] All components use exact Tailwind classes from docs/ui-spec.md
- [ ] ARIA attributes on interactive elements
- [ ] `npx tsc --noEmit` = 0 errors
- [ ] `npm run lint` = 0 errors

---

## Files to Be Created / Modified

Relative to `frontend/`:

**New files:**
```
src/types/chat.ts
src/hooks/useChat.ts
src/components/MessageList.tsx
src/components/UserMessage.tsx
src/components/BotMessage.tsx
src/components/SourceCitations.tsx
src/components/ThinkingIndicator.tsx
```

**Modified files:**
```
src/components/ChatArea.tsx    -- wire up useChat, render MessageList
src/components/ChatInput.tsx   -- enhance from disabled placeholder to functional input
src/app/globals.css            -- add thinking-dot animation-delay classes
```

---

## Known Constraints

- No actual SSE/backend integration -- use local state with mock data for testing
- Mock/demo mode: typing a message adds it as user message, shows thinking indicator for 1 second, then shows a hardcoded bot response with sample sources
- This lets us verify all UI states without a backend
- No markdown rendering in bot messages yet (plain text only) -- can be added in F4
- Desktop-first, minimum 1280px viewport

---

## Out of Scope

- SSE stream handling (Sprint F4)
- Chat history sent to backend (Sprint F4)
- Knowledge Base panel (Sprint F2)
- Markdown rendering in bot messages (Sprint F4)
- Any backend work
