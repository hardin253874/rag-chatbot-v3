export interface SearchResult {
  content: string;
  source: string;
  project?: string;
  score: number;
}

interface SseEvent {
  type: string;
  message: string;
  chunks?: number;
}

export class RagApiClient {
  constructor(private baseUrl: string) {}

  /**
   * Ingest raw text content into the knowledge base.
   * POST /ingest/text — returns SSE stream or JSON pre-check response.
   */
  async ingestText(
    content: string,
    source: string,
    project?: string,
    chunkingMode?: string
  ): Promise<string> {
    const body: Record<string, string> = { content, source };
    if (project) body.project = project;
    if (chunkingMode) body.chunkingMode = chunkingMode;

    const response = await fetch(`${this.baseUrl}/ingest/text`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });

    if (!response.ok) {
      const text = await response.text();
      throw new Error(`Ingest text failed (${response.status}): ${text}`);
    }

    return this.readIngestResponse(response);
  }

  /**
   * Ingest a web page URL into the knowledge base.
   * POST /ingest — returns SSE stream or JSON pre-check response.
   */
  async ingestUrl(
    url: string,
    project?: string,
    chunkingMode?: string
  ): Promise<string> {
    const body: Record<string, string> = { url };
    if (project) body.project = project;
    if (chunkingMode) body.chunkingMode = chunkingMode;

    const response = await fetch(`${this.baseUrl}/ingest`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });

    if (!response.ok) {
      const text = await response.text();
      throw new Error(`Ingest URL failed (${response.status}): ${text}`);
    }

    return this.readIngestResponse(response);
  }

  /**
   * Search the knowledge base for relevant chunks.
   * GET /search?query=...&project=...&top_k=...
   */
  async search(
    query: string,
    project?: string,
    topK?: number
  ): Promise<SearchResult[]> {
    const params = new URLSearchParams({ query });
    if (project) params.set("project", project);
    if (topK !== undefined) params.set("top_k", String(topK));

    const response = await fetch(`${this.baseUrl}/search?${params.toString()}`);

    if (!response.ok) {
      const text = await response.text();
      throw new Error(`Search failed (${response.status}): ${text}`);
    }

    const data = (await response.json()) as { results: SearchResult[] };
    return data.results;
  }

  /**
   * List all ingested document sources.
   * GET /ingest/sources
   */
  async listSources(): Promise<string[]> {
    const response = await fetch(`${this.baseUrl}/ingest/sources`);

    if (!response.ok) {
      const text = await response.text();
      throw new Error(`List sources failed (${response.status}): ${text}`);
    }

    const data = (await response.json()) as { sources: string[] };
    return data.sources;
  }

  /**
   * List all available project tags.
   * GET /ingest/projects
   */
  async listProjects(): Promise<string[]> {
    const response = await fetch(`${this.baseUrl}/ingest/projects`);

    if (!response.ok) {
      const text = await response.text();
      throw new Error(`List projects failed (${response.status}): ${text}`);
    }

    const data = (await response.json()) as { projects: string[] };
    return data.projects;
  }

  /**
   * Read an ingest response — handles both JSON pre-check and SSE stream formats.
   */
  private async readIngestResponse(response: Response): Promise<string> {
    const contentType = response.headers.get("content-type") ?? "";

    // JSON pre-check response (duplicate / already exists)
    if (contentType.includes("application/json")) {
      const data = (await response.json()) as { message?: string; type?: string };
      return data.message ?? JSON.stringify(data);
    }

    // SSE stream — read line by line, collect status messages, return the "done" message
    if (contentType.includes("text/event-stream")) {
      return this.readSseStream(response);
    }

    // Fallback: return raw text
    return response.text();
  }

  /**
   * Read an SSE stream from the response body, collecting events and returning the final "done" message.
   */
  private async readSseStream(response: Response): Promise<string> {
    const body = response.body;
    if (!body) {
      throw new Error("No response body for SSE stream");
    }

    const reader = body.getReader();
    const decoder = new TextDecoder();
    const statusMessages: string[] = [];
    let doneMessage = "";

    let buffer = "";

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });

      // Process complete lines
      const lines = buffer.split("\n");
      // Keep the last potentially incomplete line in the buffer
      buffer = lines.pop() ?? "";

      for (const line of lines) {
        const trimmed = line.trim();
        if (!trimmed.startsWith("data:")) continue;

        const jsonStr = trimmed.slice(5).trim();
        if (!jsonStr) continue;

        try {
          const event = JSON.parse(jsonStr) as SseEvent;
          if (event.type === "done") {
            doneMessage = event.message;
          } else if (event.type === "status") {
            statusMessages.push(event.message);
          }
        } catch {
          // Skip malformed SSE data lines
        }
      }
    }

    if (doneMessage) {
      return doneMessage;
    }

    // If no "done" event, return the last status message
    if (statusMessages.length > 0) {
      return statusMessages[statusMessages.length - 1];
    }

    return "Ingestion completed (no status message received)";
  }
}
