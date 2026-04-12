# RAG Chatbot v3 — MCP Server

An MCP (Model Context Protocol) server that exposes the RAG Chatbot v3 backend as tools for AI agents. The server wraps the existing .NET backend REST API, allowing agents to ingest documents and search the knowledge base.

## Architecture

```
AI Agent (Claude Code, Cursor, etc.)
    | MCP Protocol (HTTP/SSE)
MCP Server (TypeScript)
    | REST API calls
Backend API Server (.NET 9)
    |
Pinecone (vector store)
```

## Setup

```bash
npm install
npm run build
```

## Running

```bash
# Development (with hot reload)
npm run dev

# Production
npm run build
npm start
```

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `RAG_API_URL` | `http://localhost:3010` | Backend API base URL |
| `MCP_PORT` | `3020` | Port for the MCP server |

## Configure in Claude Code

Add to your `.mcp.json`:

```json
{
  "mcpServers": {
    "rag-chatbot": {
      "type": "sse",
      "url": "http://localhost:3020/sse"
    }
  }
}
```

For a remote deployment:

```json
{
  "mcpServers": {
    "rag-chatbot": {
      "type": "sse",
      "url": "https://your-deployment-url/sse"
    }
  }
}
```

## Available Tools

### ingest_document

Ingest raw text content into the knowledge base.

| Parameter | Required | Description |
|-----------|----------|-------------|
| `content` | Yes | The full text content of the document |
| `source` | Yes | Document name/identifier (e.g., 'readme.md') |
| `project` | No | Project tag (e.g., 'NESA') |
| `chunkingMode` | No | Chunking strategy: fixed, nlp (default), hybrid, smart |

### ingest_url

Ingest a web page into the knowledge base.

| Parameter | Required | Description |
|-----------|----------|-------------|
| `url` | Yes | URL of the web page to ingest |
| `project` | No | Project tag |
| `chunkingMode` | No | Chunking strategy: fixed, nlp (default), hybrid, smart |

### search_knowledge_base

Search the knowledge base for relevant document chunks.

| Parameter | Required | Description |
|-----------|----------|-------------|
| `query` | Yes | Search query text |
| `project` | No | Filter results to a specific project |
| `top_k` | No | Number of results (default: 8, max: 20) |

### list_sources

List all document sources ingested into the knowledge base. No parameters.

### list_projects

List all available project tags. No parameters.

## Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/sse` | SSE connection for MCP clients |
| POST | `/messages` | JSON-RPC message handling |
| GET | `/health` | Health check |
