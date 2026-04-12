import type { RagApiClient } from "../api-client.js";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

export function registerListSources(server: McpServer, apiClient: RagApiClient): void {
  server.tool(
    "list_sources",
    "List all document sources that have been ingested into the knowledge base.",
    async () => {
      try {
        const sources = await apiClient.listSources();

        if (sources.length === 0) {
          return { content: [{ type: "text" as const, text: "No sources found. The knowledge base is empty." }] };
        }

        const formatted = `Sources (${sources.length}):\n${sources.map((s) => `  - ${s}`).join("\n")}`;
        return { content: [{ type: "text" as const, text: formatted }] };
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        return { content: [{ type: "text" as const, text: `Error: ${message}` }], isError: true };
      }
    }
  );
}
