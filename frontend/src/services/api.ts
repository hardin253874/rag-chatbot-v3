import type {
  IngestSseEvent,
  IngestPreCheckResponse,
} from "@/types/activity";

const API_URL = process.env.NEXT_PUBLIC_API_URL || "http://localhost:3010";

// Re-export API_URL for use by other services (e.g., useConfig)
export { API_URL };

interface SourcesResponse {
  success: boolean;
  sources: string[];
}

interface IngestResponse {
  success: boolean;
  message: string;
}

interface ErrorBody {
  error?: string;
  detail?: string;
}

async function extractErrorMessage(
  response: Response,
  fallback: string
): Promise<string> {
  try {
    const body = (await response.json()) as ErrorBody;
    if (body.detail) return body.detail;
    if (body.error) return body.error;
  } catch {
    // Response body was not valid JSON
  }
  return `${fallback}: ${response.statusText || `HTTP ${response.status}`}`;
}

/**
 * Read SSE events from a response body and call onEvent for each parsed event.
 * Follows the same line-parsing pattern as chatApi.ts.
 */
async function readIngestSseStream(
  response: Response,
  onEvent: (event: IngestSseEvent) => void
): Promise<void> {
  if (!response.body) {
    throw new Error("Response body is empty");
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      const lines = buffer.split("\n");
      buffer = lines.pop() || "";

      for (const line of lines) {
        const trimmed = line.trim();
        if (!trimmed.startsWith("data: ")) continue;

        const json = trimmed.slice(6);
        try {
          const event = JSON.parse(json) as IngestSseEvent;
          onEvent(event);
        } catch {
          // Skip malformed JSON lines
        }
      }
    }

    // Process any remaining data in buffer
    if (buffer.trim().startsWith("data: ")) {
      const json = buffer.trim().slice(6);
      try {
        const event = JSON.parse(json) as IngestSseEvent;
        onEvent(event);
      } catch {
        // Skip malformed final line
      }
    }
  } finally {
    reader.releaseLock();
  }
}

/**
 * Ingest a URL. The backend may return:
 * - JSON (pre-check: duplicate or exists) -> returns IngestPreCheckResponse
 * - SSE stream (new or replace) -> calls onEvent for each event, returns null
 */
export async function ingestUrl(
  url: string,
  chunkingMode?: string,
  onEvent?: (event: IngestSseEvent) => void,
  replace?: boolean,
  project?: string
): Promise<IngestPreCheckResponse | null> {
  const body: Record<string, string> = { url };
  if (chunkingMode) {
    body.chunkingMode = chunkingMode;
  }
  if (project) {
    body.project = project;
  }

  const queryParams = replace ? "?replace=true" : "";

  const response = await fetch(`${API_URL}/ingest${queryParams}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });

  if (!response.ok) {
    const message = await extractErrorMessage(response, "Ingestion failed");
    throw new Error(message);
  }

  const contentType = response.headers.get("Content-Type") || "";

  if (contentType.includes("application/json")) {
    const data = (await response.json()) as IngestPreCheckResponse;
    return data;
  }

  // SSE stream
  if (onEvent) {
    await readIngestSseStream(response, onEvent);
  }
  return null;
}

/**
 * Ingest a file. The backend may return:
 * - JSON (pre-check: duplicate or exists) -> returns IngestPreCheckResponse
 * - SSE stream (new or replace) -> calls onEvent for each event, returns null
 */
export async function ingestFile(
  file: File,
  chunkingMode?: string,
  onEvent?: (event: IngestSseEvent) => void,
  replace?: boolean,
  project?: string
): Promise<IngestPreCheckResponse | null> {
  const formData = new FormData();
  formData.append("file", file);
  if (chunkingMode) {
    formData.append("chunkingMode", chunkingMode);
  }
  if (project) {
    formData.append("project", project);
  }

  const queryParams = replace ? "?replace=true" : "";

  const response = await fetch(`${API_URL}/ingest${queryParams}`, {
    method: "POST",
    body: formData,
  });

  if (!response.ok) {
    const message = await extractErrorMessage(response, "File upload failed");
    throw new Error(message);
  }

  const contentType = response.headers.get("Content-Type") || "";

  if (contentType.includes("application/json")) {
    const data = (await response.json()) as IngestPreCheckResponse;
    return data;
  }

  // SSE stream
  if (onEvent) {
    await readIngestSseStream(response, onEvent);
  }
  return null;
}

export async function listSources(): Promise<SourcesResponse> {
  try {
    const response = await fetch(`${API_URL}/ingest/sources`);

    if (!response.ok) {
      return { success: false, sources: [] };
    }

    const data = (await response.json()) as SourcesResponse;
    return { success: data.success, sources: data.sources };
  } catch {
    return { success: false, sources: [] };
  }
}

export async function getProjects(): Promise<string[]> {
  try {
    const response = await fetch(`${API_URL}/ingest/projects`);
    if (!response.ok) return [];
    const data = (await response.json()) as { projects: string[] };
    return data.projects;
  } catch {
    return [];
  }
}

export async function resetKnowledgeBase(): Promise<IngestResponse> {
  try {
    const response = await fetch(`${API_URL}/ingest/reset`, {
      method: "DELETE",
    });

    if (!response.ok) {
      const message = await extractErrorMessage(response, "Reset failed");
      return { success: false, message };
    }

    const data = (await response.json()) as IngestResponse;
    return { success: data.success, message: data.message };
  } catch {
    return {
      success: false,
      message: "Network error: unable to reach server",
    };
  }
}
