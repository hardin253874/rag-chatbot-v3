"use client";

import type { AppConfig, ConnectionStatus } from "@/types/config";
import { SettingsSection } from "./SettingsSection";
import { KnowledgeBasePanel } from "./KnowledgeBasePanel";

interface SidebarProps {
  config: AppConfig | null;
  status: ConnectionStatus;
  onClearChat: () => void;
}

export function Sidebar({ config, status, onClearChat }: SidebarProps) {
  return (
    <aside
      className="w-80 bg-slate-900 border-r border-slate-800 flex flex-col h-[calc(100vh-56px)] overflow-hidden"
      role="complementary"
      aria-label="Sidebar"
    >
      <SettingsSection config={config} status={status} />
      <KnowledgeBasePanel onClearChat={onClearChat} />
    </aside>
  );
}
