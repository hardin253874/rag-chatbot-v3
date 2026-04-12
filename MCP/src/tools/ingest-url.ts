import { z } from "zod";
import type { RagApiClient } from "../api-client.js";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

const inputSchema = {
  url: z.string().describe("The URL of the web page to ingest"),
  project: z
    .string()
    .optional()
    .describe("Optional project tag to organize the document"),
  chunkingMode: z
    .enum(["fixed", "nlp", "hybrid", "smart"])
    .optional()
    .describe("Chunking strategy (default: 'nlp')"),
};

export function registerIngestUrl(server: McpServer, apiClient: RagApiClient): void {
  server.tool(
    "ingest_url",
    "Ingest a web page into the RAG knowledge base. The page content is fetched, chunked, and stored as vector embeddings.",
    inputSchema,
    async ({ url, project, chunkingMode }) => {
      try {
        const result = await apiClient.ingestUrl(url, project, chunkingMode);
        return { content: [{ type: "text" as const, text: result }] };
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        return { content: [{ type: "text" as const, text: `Error: ${message}` }], isError: true };
      }
    }
  );
}
