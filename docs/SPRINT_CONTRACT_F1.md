# Sprint Contract F1 -- Scaffold + Layout + Settings

**Agent:** Frontend Developer
**Date:** 2026-03-31
**Status:** Complete

---

## What Will Be Built

1. **Next.js 14 project** with App Router, TypeScript strict mode, Tailwind CSS, ESLint
2. **Main layout** -- header (56px) + sidebar (320px) + chat area (remaining space)
3. **Header** -- app name "RAG Chatbot", subtitle "v3", connection status indicator (dot + label)
4. **Sidebar** -- dark (slate-900), contains Settings section with LLM info display
5. **Settings section** -- displays LLM model name and base URL fetched from GET /config
6. **Chat area** -- empty state placeholder with icon + text (no chat functionality yet)
7. **Chat input bar** -- visual placeholder (disabled, non-functional)
8. **Config fetching** -- custom hook calls GET /config on load, graceful fallback on failure
9. **Styling** -- matches docs/ui-spec.md exactly (dark sidebar, light content, indigo accent)

---

## Definition of Done

- [ ] `npm run dev` starts app on http://localhost:3000
- [ ] Page renders header with app name "RAG Chatbot" and subtitle "v3"
- [ ] Header shows connection status indicator (green/Connected or amber/Connecting or red/Disconnected)
- [ ] Sidebar (320px, bg-slate-900) with Settings section visible
- [ ] Settings section shows "SETTINGS" label, LLM info block with Model and Base URL
- [ ] Main chat area takes remaining space with bg-slate-50
- [ ] Chat area shows empty state: icon + "Ask a question about your documents" + subtitle
- [ ] Chat input bar visible at bottom (disabled placeholder)
- [ ] App fetches GET /config on load (from NEXT_PUBLIC_API_URL env var)
- [ ] If config fetch fails, app renders with defaults (no crash), status shows "Disconnected"
- [ ] If config fetch succeeds, LLM info shows model name and base URL, status shows "Connected"
- [ ] While config is loading, LLM info shows "Loading..." with pulse animation
- [ ] TypeScript strict mode, `npx tsc --noEmit` = 0 errors
- [ ] `npm run lint` = 0 errors
- [ ] Fonts: Inter (UI) and JetBrains Mono (monospace) loaded via next/font/google
- [ ] Tailwind config includes custom keyframes, font families per docs/ui-spec.md
- [ ] ARIA attributes on sidebar, chat area, status indicator, chat input, send button

---

## Files to Be Created

Relative to `frontend/`:

```
package.json
tsconfig.json
tailwind.config.ts
postcss.config.mjs
next.config.ts
.env.local.example
src/app/layout.tsx
src/app/page.tsx
src/app/globals.css
src/components/Header.tsx
src/components/Sidebar.tsx
src/components/SettingsSection.tsx
src/components/ChatArea.tsx
src/components/EmptyState.tsx
src/components/ChatInput.tsx
src/components/StatusIndicator.tsx
src/hooks/useConfig.ts
src/types/config.ts
```

---

## Known Constraints

- Backend may not be running yet -- config fetch must gracefully fail with sensible defaults
- Desktop-first, minimum 1280px viewport
- No chat functionality yet -- just layout shell with disabled input
- Knowledge Base panel is NOT in this sprint (F2)
- No message display, no SSE streaming -- just the empty state
- lucide-react needed for icons (MessageSquare, ArrowUp)

---

## Out of Scope

- Knowledge Base panel (Sprint F2)
- Chat message display and streaming (Sprint F3)
- SSE handling and chat history (Sprint F4)
- Any backend work
