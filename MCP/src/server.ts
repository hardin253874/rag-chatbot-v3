import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { RagApiClient } from "./api-client.js";
import { registerIngestDocument } from "./tools/ingest-document.js";
import { registerIngestUrl } from "./tools/ingest-url.js";
import { registerSearch } from "./tools/search.js";
import { registerListSources } from "./tools/list-sources.js";
import { registerListProjects } from "./tools/list-projects.js";

/**
 * Create and configure the MCP server with all tools registered.
 */
export function createMcpServer(apiClient: RagApiClient): McpServer {
  const server = new McpServer({
    name: "rag-chatbot-v3",
    version: "1.0.0",
  });

  // Register all tools
  registerIngestDocument(server, apiClient);
  registerIngestUrl(server, apiClient);
  registerSearch(server, apiClient);
  registerListSources(server, apiClient);
  registerListProjects(server, apiClient);

  return server;
}
