"use client";

import { useState, useEffect, useCallback } from "react";
import type { AppConfig, ConnectionStatus } from "@/types/config";

interface UseConfigReturn {
  config: AppConfig | null;
  status: ConnectionStatus;
  error: string | null;
}

const DEFAULT_CONFIG: AppConfig = {
  rewriteLlm: {
    model: "Unavailable",
    baseUrl: "Unavailable",
  },
};

export function useConfig(): UseConfigReturn {
  const [config, setConfig] = useState<AppConfig | null>(null);
  const [status, setStatus] = useState<ConnectionStatus>("connecting");
  const [error, setError] = useState<string | null>(null);

  const fetchConfig = useCallback(async () => {
    const apiUrl = process.env.NEXT_PUBLIC_API_URL || "http://localhost:3010";

    try {
      setStatus("connecting");

      const response = await fetch(`${apiUrl}/config`, {
        method: "GET",
        headers: {
          Accept: "application/json",
        },
        signal: AbortSignal.timeout(5000),
      });

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
      }

      const data: AppConfig = await response.json();
      setConfig(data);
      setStatus("connected");
      setError(null);
    } catch (err) {
      const message =
        err instanceof Error ? err.message : "Failed to fetch config";
      setConfig(DEFAULT_CONFIG);
      setStatus("disconnected");
      setError(message);
    }
  }, []);

  useEffect(() => {
    fetchConfig();
  }, [fetchConfig]);

  return { config, status, error };
}
