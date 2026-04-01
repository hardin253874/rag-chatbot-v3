const API_URL = process.env.NEXT_PUBLIC_API_URL || "http://localhost:3010";

// Re-export API_URL for use by other services (e.g., useConfig)
export { API_URL };

interface IngestResponse {
  success: boolean;
  message: string;
}

interface SourcesResponse {
  success: boolean;
  sources: string[];
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

export async function ingestUrl(
  url: string,
  chunkingMode?: string
): Promise<IngestResponse> {
  try {
    const body: Record<string, string> = { url };
    if (chunkingMode) {
      body.chunkingMode = chunkingMode;
    }

    const response = await fetch(`${API_URL}/ingest`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });

    if (!response.ok) {
      const message = await extractErrorMessage(response, "Ingestion failed");
      return { success: false, message };
    }

    const data = (await response.json()) as IngestResponse;
    return { success: data.success, message: data.message };
  } catch {
    return { success: false, message: "Network error: unable to reach server" };
  }
}

export async function ingestFile(
  file: File,
  chunkingMode?: string
): Promise<IngestResponse> {
  try {
    const formData = new FormData();
    formData.append("file", file);
    if (chunkingMode) {
      formData.append("chunkingMode", chunkingMode);
    }

    const response = await fetch(`${API_URL}/ingest`, {
      method: "POST",
      body: formData,
    });

    if (!response.ok) {
      const message = await extractErrorMessage(response, "File upload failed");
      return { success: false, message };
    }

    const data = (await response.json()) as IngestResponse;
    return { success: data.success, message: data.message };
  } catch {
    return { success: false, message: "Network error: unable to reach server" };
  }
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
