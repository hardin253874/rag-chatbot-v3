# Sprint Contract B3 -- Pinecone Integration

**Sprint:** B3
**Agent:** Backend Developer
**Date:** 2026-03-31
**Status:** Complete

---

## What Will Be Built

Pinecone vector store service that connects to the `rag-chatbot-v3` index using the REST API directly (no SDK). Uses `IHttpClientFactory` for HTTP calls. Interface in Core, implementation in Infrastructure.

### Interface (Core)

- `IPineconeService` -- store documents, similarity search, list sources, reset collection

### Implementation (Infrastructure)

- `PineconeService` -- HTTP client talking to Pinecone REST API
  - StoreDocuments: upsert records with `_id`, `chunk_text`, `source` fields, batched in groups of 96
  - SimilaritySearch: text query via integrated embedding, returns top K results as Documents
  - ListSources: broad search with top_k 100, extract unique source values
  - ResetCollection: delete all records in namespace

### Configuration

- Add `PineconeHost` to AppConfig (default: `rag-chatbot-v3-y3gph8e.svc.aped-4627-b74a.pinecone.io`)
- Add `PineconeNamespace` to AppConfig (default: `rag-chatbot`)

### DI Registration

- Register named HttpClient `"Pinecone"` with base URL and default headers
- Register `IPineconeService` -> `PineconeService`

---

## Definition of Done

- [x] `IPineconeService` interface defined in Core with 4 methods
- [x] `PineconeService` implementation in Infrastructure using `IHttpClientFactory`
- [x] `AppConfig` extended with `PineconeHost` and `PineconeNamespace`
- [x] StoreDocuments upserts records with `_id`, `chunk_text`, `source` in batches of 96
- [x] SimilaritySearch sends text query, returns up to K results with PageContent and source
- [x] ListSources returns deduplicated list of source strings
- [x] ResetCollection deletes all records in namespace
- [x] Pinecone handles embedding (backend does NOT call any embedding API)
- [x] HTTP errors throw meaningful exceptions
- [x] Unit tests for all service methods pass (mocked HttpClient)
- [x] `dotnet build` = 0 errors
- [x] `dotnet test` = all pass

---

## Files to Be Created

Paths relative to `backend/`:

```
RagChatbot.Core/
  Interfaces/
    IPineconeService.cs

RagChatbot.Infrastructure/
  VectorStore/
    PineconeService.cs

RagChatbot.Tests/
  VectorStore/
    PineconeServiceTests.cs
```

---

## Files to Be Modified

```
RagChatbot.Core/Configuration/AppConfig.cs          -- add PineconeHost, PineconeNamespace
RagChatbot.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs -- register HttpClient + PineconeService
RagChatbot.Api/Program.cs                           -- wire PineconeHost/Namespace into AppConfig options
```

---

## Technical Notes

### Pinecone REST API

- Host: `https://{PineconeHost}`
- Headers: `Api-Key`, `Content-Type: application/json`, `X-Pinecone-API-Version: 2025-01`
- Upsert: `POST /records/namespaces/{namespace}/upsert` -- body `{ "records": [...] }`
- Search: `POST /records/namespaces/{namespace}/search` -- body with `query.top_k`, `query.inputs.text`, `fields`
- Delete all: `POST /vectors/delete` -- body `{ "deleteAll": true, "namespace": "..." }`
- Search response: `result.hits[]` with `_id`, `_score`, `fields.chunk_text`, `fields.source`

### Batching

- Max 96 records per upsert call
- Split input list, make multiple HTTP calls for larger batches

### ListSources Approach

- Use a broad search query ("document") with top_k: 100
- Extract unique `source` values from results
- Practical limit of ~100 unique sources (acceptable for MVP)

### Error Handling

- Throw `HttpRequestException` with context on non-success HTTP responses
- Include status code and response body in exception message

---

## Known Constraints

- Per ADR-001: Pinecone only, no abstraction layer needed
- Per functional spec 4.4: Pinecone handles embedding via integrated model
- No embedding API calls from backend
- Batch size 96 per Pinecone documentation
