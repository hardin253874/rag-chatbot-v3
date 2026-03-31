"use client";

import type { AppConfig, ConnectionStatus } from "@/types/config";

interface SettingsSectionProps {
  config: AppConfig | null;
  status: ConnectionStatus;
}

export function SettingsSection({ config, status }: SettingsSectionProps) {
  const isLoading = status === "connecting";
  const isError = status === "disconnected";

  const modelValue = config?.rewriteLlm.model ?? "Unavailable";
  const baseUrlValue = config?.rewriteLlm.baseUrl ?? "Unavailable";

  return (
    <div className="px-5 py-4 flex-shrink-0">
      <h2 className="text-xs font-semibold uppercase tracking-wider text-slate-400 mb-3">
        Settings
      </h2>

      <div className="bg-slate-800 rounded-lg p-3 space-y-2">
        <span className="text-xs font-semibold uppercase tracking-wider text-slate-400">
          LLM
        </span>

        <div className="flex items-center justify-between">
          <span className="text-xs text-slate-400">Model</span>
          {isLoading ? (
            <span className="text-xs font-mono text-slate-500 animate-pulse">
              Loading...
            </span>
          ) : isError && modelValue === "Unavailable" ? (
            <span className="text-xs font-mono text-red-400">Unavailable</span>
          ) : (
            <span className="text-xs font-mono text-slate-50">
              {modelValue}
            </span>
          )}
        </div>

        <div className="flex items-center justify-between">
          <span className="text-xs text-slate-400">Base URL</span>
          {isLoading ? (
            <span className="text-xs font-mono text-slate-500 animate-pulse">
              Loading...
            </span>
          ) : isError && baseUrlValue === "Unavailable" ? (
            <span className="text-xs font-mono text-red-400">Unavailable</span>
          ) : (
            <span className="text-xs font-mono text-slate-50 truncate max-w-[180px]">
              {baseUrlValue}
            </span>
          )}
        </div>
      </div>
    </div>
  );
}
