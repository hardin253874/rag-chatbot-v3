"use client";

import type { ConnectionStatus } from "@/types/config";
import { StatusIndicator } from "./StatusIndicator";

interface HeaderProps {
  status: ConnectionStatus;
}

export function Header({ status }: HeaderProps) {
  return (
    <header className="h-14 bg-white border-b border-slate-200 px-6 flex items-center justify-between">
      <div className="flex items-center gap-3">
        <h1 className="text-xl font-semibold text-slate-900 leading-7">
          RAG Chatbot
        </h1>
        <span className="text-sm text-slate-400 leading-5">v3.5</span>
      </div>
      <StatusIndicator status={status} />
    </header>
  );
}
