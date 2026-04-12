import { z } from "zod";
import type { RagApiClient } from "../api-client.js";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

const inputSchema = {
  content: z.string().describe("The full text content of the document to ingest"),
  source: z.string().describe("A name/identifier for the document (e.g., 'readme.md', 'api-docs.txt')"),
  project: z
    .string()
    .optional()
    .describe("Optional project tag to organize the document (e.g., 'NESA'). Normalized to uppercase with dashes."),
  chunkingMode: z
    .enum(["fixed", "nlp", "hybrid", "smart"])
    .optional()
    .describe("Chunking strategy: 'fixed' (character split), 'nlp' (sentence boundaries, default), 'hybrid' (NLP + LLM), 'smart' (full LLM analysis)"),
};

export function registerIngestDocument(server: McpServer, apiClient: RagApiClient): void {
  server.tool(
    "ingest_document",
    "Ingest a text document into the RAG knowledge base. The document is chunked and stored as vector embeddings for later retrieval.",
    inputSchema,
    async ({ content, source, project, chunkingMode }) => {
      try {
        const result = await apiClient.ingestText(content, source, project, chunkingMode);
        return { content: [{ type: "text" as const, text: result }] };
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        return { content: [{ type: "text" as const, text: `Error: ${message}` }], isError: true };
      }
    }
  );
}
