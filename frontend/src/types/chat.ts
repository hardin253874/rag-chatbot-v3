export interface ChatMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  sources?: string[];
}

/** A single SSE event from the POST /chat stream. */
export interface SseEvent {
  type: "chunk" | "sources" | "done";
  text?: string;
  sources?: string[];
}

/** A history entry sent with each /chat request (no id, no sources -- just role+content). */
export interface ChatHistoryEntry {
  role: "user" | "assistant";
  content: string;
}
