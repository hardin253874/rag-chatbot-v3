export interface ActivityEntry {
  id: string;
  type: "info" | "success" | "error";
  message: string;
  timestamp: Date;
}
