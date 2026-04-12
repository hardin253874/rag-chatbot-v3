import type { RagApiClient } from "../api-client.js";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

export function registerListProjects(server: McpServer, apiClient: RagApiClient): void {
  server.tool(
    "list_projects",
    "List all available project tags in the knowledge base.",
    async () => {
      try {
        const projects = await apiClient.listProjects();

        if (projects.length === 0) {
          return { content: [{ type: "text" as const, text: "No projects found. The knowledge base is empty." }] };
        }

        const formatted = `Projects (${projects.length}):\n${projects.map((p) => `  - ${p}`).join("\n")}`;
        return { content: [{ type: "text" as const, text: formatted }] };
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        return { content: [{ type: "text" as const, text: `Error: ${message}` }], isError: true };
      }
    }
  );
}
