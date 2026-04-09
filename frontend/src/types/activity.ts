export interface ActivityEntry {
  id: string;
  type: "info" | "success" | "error";
  message: string;
  timestamp: Date;
}

/** A single SSE event from the POST /ingest stream. */
export interface IngestSseEvent {
  type: "status" | "done" | "error";
  message: string;
  chunks?: number;
}

/** JSON response from /ingest when a pre-check detects duplicate or existing source. */
export interface IngestPreCheckResponse {
  status: "duplicate" | "exists";
  message: string;
  source?: string;
}
