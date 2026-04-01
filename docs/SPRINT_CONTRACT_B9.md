# Sprint Contract B9 — Agentic Pipeline + DI Registration

## Goal
Create AgenticRagPipelineService implementing the agent loop, update DI to replace the old pipeline.

## Deliverables
1. AgenticRagPipelineService : IRagPipelineService with agent loop (max 3 iterations)
2. DI: register new pipeline and tools, remove ConversationalDetector from DI
3. Keep old RagPipelineService and ConversationalDetector in codebase

## Success Criteria
- dotnet build: 0 errors, 0 warnings
- dotnet test: all 154 existing + ~8 new tests pass
- Old tests still pass (class-level, not DI-dependent)

## Status: In Progress
