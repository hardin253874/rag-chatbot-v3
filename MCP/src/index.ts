import express from "express";
import { SSEServerTransport } from "@modelcontextprotocol/sdk/server/sse.js";
import { RagApiClient } from "./api-client.js";
import { createMcpServer } from "./server.js";

const apiUrl = process.env.RAG_API_URL ?? "http://localhost:3010";
const port = parseInt(process.env.MCP_PORT ?? "3020", 10);

const apiClient = new RagApiClient(apiUrl);
const mcpServer = createMcpServer(apiClient);

const app = express();
app.use(express.json());

// Store active SSE transports by session ID
const transports: Record<string, SSEServerTransport> = {};

// SSE endpoint — MCP clients connect here to establish the event stream
app.get("/sse", async (_req, res) => {
  const transport = new SSEServerTransport("/messages", res);
  transports[transport.sessionId] = transport;

  res.on("close", () => {
    delete transports[transport.sessionId];
  });

  await mcpServer.connect(transport);
});

// Messages endpoint — MCP clients POST JSON-RPC messages here
app.post("/messages", async (req, res) => {
  const sessionId = req.query.sessionId as string;
  const transport = transports[sessionId];

  if (!transport) {
    res.status(400).json({ error: "No transport found for sessionId" });
    return;
  }

  await transport.handlePostMessage(req, res);
});

// Health check
app.get("/health", (_req, res) => {
  res.json({ status: "ok", backendUrl: apiUrl });
});

app.listen(port, () => {
  console.log(`MCP server running on http://localhost:${port}/sse`);
  console.log(`Backend API: ${apiUrl}`);
});
