"use client";

import type { ConnectionStatus } from "@/types/config";

interface StatusIndicatorProps {
  status: ConnectionStatus;
}

const STATUS_CONFIG: Record<
  ConnectionStatus,
  { dotClass: string; label: string }
> = {
  connected: {
    dotClass: "bg-green-500",
    label: "Connected",
  },
  disconnected: {
    dotClass: "bg-red-500",
    label: "Disconnected",
  },
  connecting: {
    dotClass: "bg-amber-500 animate-pulse",
    label: "Connecting...",
  },
};

export function StatusIndicator({ status }: StatusIndicatorProps) {
  const { dotClass, label } = STATUS_CONFIG[status];

  return (
    <div
      className="flex items-center gap-2"
      role="status"
      aria-label={`Connection status: ${label}`}
    >
      <span className={`w-2 h-2 rounded-full ${dotClass}`} />
      <span className="text-xs text-slate-500 leading-4">{label}</span>
    </div>
  );
}
