# Sprint Contract F2 -- Knowledge Base Panel (UI Shell)

**Sprint:** F2
**Agent:** Frontend Developer
**Date:** 2026-03-31
**Status:** In Progress

---

## Goal

Build the Knowledge Base panel in the sidebar with URL ingestion, file upload, activity log, resource list, and clear KB functionality. All API calls are stubbed (backend B4 not ready).

## Deliverables

### New Files

| File | Purpose |
|------|---------|
| `src/types/activity.ts` | ActivityEntry type definition |
| `src/services/api.ts` | Stubbed API service layer (ingestUrl, ingestFile, listSources, resetKnowledgeBase) |
| `src/hooks/useKnowledgeBase.ts` | KB state management hook (activity log, resources, loading states) |
| `src/components/KnowledgeBasePanel.tsx` | Container for all KB UI in sidebar |
| `src/components/UrlIngest.tsx` | URL text input + "Add URL" button |
| `src/components/FileUpload.tsx` | File input (.md, .txt) + "Upload File" button |
| `src/components/ActivityLog.tsx` | Scrollable log with color-coded entries |
| `src/components/ResourceList.tsx` | Scrollable list of ingested sources |
| `src/components/ConfirmDialog.tsx` | Reusable modal confirmation dialog |

### Modified Files

| File | Change |
|------|--------|
| `src/components/Sidebar.tsx` | Add KnowledgeBasePanel below Settings, accept clearChat prop |
| `src/hooks/useChat.ts` | Expose clearMessages() function |
| `src/components/ChatArea.tsx` | Accept clearChat callback, pass to parent |
| `src/app/page.tsx` | Wire clearChat from useChat to Sidebar via prop drilling |

## Definition of Done

- [x] KB panel renders in sidebar below Settings section with border-t divider
- [x] URL input + "Add URL" button visible with correct sidebar styling
- [x] File input accepts only .md and .txt files + "Upload File" button
- [x] Clicking "Add URL" with non-empty URL logs info entry then calls stubbed API
- [x] Clicking "Upload File" with selected file logs info entry then calls stubbed API
- [x] Activity log shows color-coded entries: blue info, green success, red error
- [x] Activity log starts with "Ready. Add documents to get started."
- [x] "List Resources" button shows "Loading..." then results (stubbed mock data)
- [x] "No resources found." shown when list is empty
- [x] "Clear Knowledge Base" shows confirmation dialog before action
- [x] Confirmation dialog matches docs/ui-spec.md design exactly
- [x] After successful KB clear, chat history is also cleared
- [x] All components styled per docs/ui-spec.md
- [x] ARIA attributes on all interactive elements
- [x] `npx tsc --noEmit` = 0 errors
- [x] `npm run lint` = 0 errors

## API Stubs

All API functions simulate 500ms delay then return success. Mock data for listSources returns 3 example sources. Service layer designed for easy swap to real endpoints later.

## Integration Strategy

Chat clearing on KB reset uses prop drilling: `page.tsx` gets `clearMessages` from `useChat`, passes it as `onClearChat` prop through `Sidebar` to `KnowledgeBasePanel` to `useKnowledgeBase`.

## Constraints

- Backend B4 (ingestion endpoints) not ready -- all API calls stubbed
- No `console.log` in production paths
- TypeScript strict mode -- no `any` types
- NEVER run `git commit`
