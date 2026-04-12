import { z } from "zod";
import type { RagApiClient } from "../api-client.js";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

const inputSchema = {
  query: z.string().describe("The search query. Use clear, specific terms for best results."),
  project: z
    .string()
    .optional()
    .describe("Optional project filter. Only search within this project's documents."),
  top_k: z
    .number()
    .optional()
    .describe("Number of results to return (default: 8, max: 20)"),
};

export function registerSearch(server: McpServer, apiClient: RagApiClient): void {
  server.tool(
    "search_knowledge_base",
    "Search the RAG knowledge base for documents relevant to a query. Returns matching chunks with content, source, project, and similarity score.",
    inputSchema,
    async ({ query, project, top_k }) => {
      try {
        const results = await apiClient.search(query, project, top_k);

        if (results.length === 0) {
          return { content: [{ type: "text" as const, text: "No results found." }] };
        }

        const formatted = results
          .map((r, i) => {
            const lines = [
              `--- Result ${i + 1} (score: ${r.score.toFixed(4)}) ---`,
              `Source: ${r.source}`,
            ];
            if (r.project) lines.push(`Project: ${r.project}`);
            lines.push(`Content:\n${r.content}`);
            return lines.join("\n");
          })
          .join("\n\n");

        return { content: [{ type: "text" as const, text: formatted }] };
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        return { content: [{ type: "text" as const, text: `Error: ${message}` }], isError: true };
      }
    }
  );
}
