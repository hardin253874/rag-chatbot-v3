# Sprint Contract B7 — LLM Service Extension

## Goal
Add ChatWithToolsAsync to ILlmService/LlmService, replace hardcoded model/API key with config, update HttpClient base URL.

## Deliverables
1. ChatWithToolsAsync method on ILlmService and LlmService
2. Replace hardcoded model "gpt-4o-mini" with config.LlmModel
3. Replace hardcoded OpenAiApiKey with config.EffectiveLlmApiKey
4. Update "OpenAI" HttpClient base URL to config.LlmBaseUrl

## Success Criteria
- dotnet build: 0 errors, 0 warnings
- dotnet test: all 142 existing + ~3 new tests pass

## Status: In Progress
