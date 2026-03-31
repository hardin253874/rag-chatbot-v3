# RAG Chatbot v3 — Cross-Agent Blockers

## Open

(none)

## Resolved

(none)

## Dependency Map

```
Backend /health, /config  ←  Frontend initialization (GET /config)
Backend /ingest            ←  Frontend KB panels (file upload, URL add)
Backend /ingest/sources    ←  Frontend List Resources button
Backend /ingest/reset      ←  Frontend Clear KB button
Backend /chat (SSE)        ←  Frontend chat interface (stream handling)
```
