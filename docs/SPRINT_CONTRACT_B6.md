# Sprint Contract B6 — Configuration + Core Models

## Goal
Add LLM configuration properties to AppConfig, create core models for tool calling (LlmToolResponse, ToolDefinition, IAgentTool), and update ConfigController to expose llm section.

## Deliverables
1. AppConfig: LlmBaseUrl, LlmModel, LlmApiKey, EffectiveLlmApiKey
2. Core models: LlmToolResponse, ToolCall, ToolDefinition
3. Core interface: IAgentTool
4. ConfigResponse + ConfigController: llm section
5. Program.cs: register new config properties

## Success Criteria
- dotnet build: 0 errors, 0 warnings
- dotnet test: all 137 existing + ~5 new tests pass
- No API keys exposed in /config

## Status: In Progress
