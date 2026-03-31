import { API_URL } from "./api";
import type { SseEvent, ChatHistoryEntry } from "@/types/chat";

/**
 * POST to /chat and yield SSE events as they arrive from the stream.
 *
 * Throws on network error or non-OK HTTP response.
 * The caller is responsible for handling each yielded SseEvent.
 */
export async function* streamChat(
  question: string,
  history: ChatHistoryEntry[],
  signal?: AbortSignal
): AsyncGenerator<SseEvent> {
  const response = await fetch(`${API_URL}/chat`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ question, history }),
    signal,
  });

  if (!response.ok) {
    let errorMessage = `Chat failed: ${response.statusText || `HTTP ${response.status}`}`;
    try {
      const body = (await response.json()) as { error?: string; detail?: string };
      if (body.detail) errorMessage = body.detail;
      else if (body.error) errorMessage = body.error;
    } catch {
      // Response body was not valid JSON -- use default message
    }
    throw new Error(errorMessage);
  }

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
          const event = JSON.parse(json) as SseEvent;
          yield event;
        } catch {
          // Skip malformed JSON lines
        }
      }
    }

    // Process any remaining data in buffer
    if (buffer.trim().startsWith("data: ")) {
      const json = buffer.trim().slice(6);
      try {
        const event = JSON.parse(json) as SseEvent;
        yield event;
      } catch {
        // Skip malformed final line
      }
    }
  } finally {
    reader.releaseLock();
  }
}
