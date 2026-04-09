export interface ChatMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  status?: string;
  sources?: string[];
  quality?: { faithfulness: number | null; contextRecall: number | null; warning?: string | null };
}

/** A single SSE event from the POST /chat stream. */
export interface SseEvent {
  type: "chunk" | "sources" | "quality" | "status" | "done";
  text?: string;
  sources?: string[];
  faithfulness?: number;
  contextRecall?: number;
  warning?: string;
}

/** A history entry sent with each /chat request (no id, no sources -- just role+content). */
export interface ChatHistoryEntry {
  role: "user" | "assistant";
  content: string;
}
