export interface AppConfig {
  rewriteLlm: {
    model: string;
    baseUrl: string;
  };
}

export type ConnectionStatus = "connected" | "disconnected" | "connecting";
