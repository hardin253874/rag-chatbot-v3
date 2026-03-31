# Sprint Contract B1 -- Clean Architecture Scaffold

**Sprint:** B1
**Agent:** Backend Developer
**Date:** 2026-03-31
**Status:** Complete

---

## What Will Be Built

A .NET 8 solution with Clean Architecture consisting of three projects:

1. **RagChatbot.Api** -- ASP.NET Core Web API host (controllers, Program.cs, CORS, env loading)
2. **RagChatbot.Core** -- Domain models, configuration classes, interfaces (no external dependencies)
3. **RagChatbot.Infrastructure** -- External service implementations (none yet, placeholder for Pinecone/OpenAI)

Plus:

- Environment variable / .env file loading using `DotNetEnv`
- CORS configured to allow any origin
- `GET /health` endpoint returning `{"status":"ok"}`
- `GET /config` endpoint returning rewrite LLM config (no secrets)
- Strongly-typed configuration class in Core
- Unit tests for both endpoints and configuration binding

---

## Definition of Done

- [x] `dotnet build` succeeds with 0 errors
- [x] `dotnet run` starts the server on PORT 3010 (from .env or default)
- [x] `GET /health` returns `{"status":"ok"}` with HTTP 200
- [x] `GET /config` returns JSON with `rewriteLlm.model` and `rewriteLlm.baseUrl`, no API keys
- [x] Solution has 3 projects with correct references: Api references Core + Infrastructure, Infrastructure references Core
- [x] Configuration bound to strongly-typed `AppConfig` class in Core
- [x] CORS allows any origin
- [x] Unit tests exist and pass (`dotnet test`) -- 12 tests, all passing

---

## Files to Be Created

Paths relative to `backend/`:

```
RagChatbot.sln

RagChatbot.Api/
  RagChatbot.Api.csproj
  Program.cs
  Controllers/
    HealthController.cs
    ConfigController.cs

RagChatbot.Core/
  RagChatbot.Core.csproj
  Configuration/
    AppConfig.cs
  Models/
    ConfigResponse.cs
    HealthResponse.cs

RagChatbot.Infrastructure/
  RagChatbot.Infrastructure.csproj
  (placeholder -- no services yet)

RagChatbot.Tests/
  RagChatbot.Tests.csproj
  HealthControllerTests.cs
  ConfigControllerTests.cs
  AppConfigTests.cs
```

---

## Known Constraints

- No Docker -- app runs via `dotnet run`
- .env file is read from the project root (parent of `backend/`), i.e., `../../../.env` relative to the Api project or resolved at runtime
- PORT default is 3010
- Per ADR-003: query rewrite is always on, no QUERY_MODE or VECTOR_STORE toggles in /config
- Per ADR-004: API keys are never exposed in /config response
- Per ADR-001: Pinecone only, but Infrastructure project is a placeholder for now

---

## Config Response Shape (per spec + brainstorm simplification)

```json
{
  "rewriteLlm": {
    "baseUrl": "https://api.openai.com/v1",
    "model": "gpt-4o-mini"
  }
}
```

Note: The original spec includes `vectorStore` and `queryMode` fields, but per brainstorm decisions (ADR-001, ADR-003), these are removed. Pinecone-only, rewrite always on.

---

## Technical Approach

- **Controllers** (not minimal APIs) for clarity and testability
- **DotNetEnv** NuGet package to load `.env` file from project root
- **Microsoft.Extensions.Options** pattern for strongly-typed config
- **WebApplicationFactory** for integration-style controller tests
- JSON property naming: camelCase (default ASP.NET Core behavior)
