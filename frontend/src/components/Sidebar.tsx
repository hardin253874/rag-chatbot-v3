"use client";

import type { AppConfig, ConnectionStatus } from "@/types/config";
import type { ActivityEntry } from "@/types/activity";
import { SettingsSection } from "./SettingsSection";
import { KnowledgeBasePanel } from "./KnowledgeBasePanel";

interface PendingReplace {
  file?: File;
  url?: string;
  source: string;
}

interface SidebarProps {
  config: AppConfig | null;
  status: ConnectionStatus;
  onClearChat: () => void;
  // Knowledge base props
  activityLog: ActivityEntry[];
  resources: string[];
  isResourcesVisible: boolean;
  isLoadingResources: boolean;
  isIngesting: boolean;
  chunkingMode: string;
  pendingReplace: PendingReplace | null;
  project: string;
  setChunkingMode: (mode: string) => void;
  setProject: (project: string) => void;
  addUrl: (url: string) => Promise<void>;
  uploadFile: (file: File) => Promise<void>;
  toggleResources: () => Promise<void>;
  clearKnowledgeBase: (onChatCleared: () => void) => Promise<void>;
  confirmReplace: () => Promise<void>;
  cancelReplace: () => void;
}

export function Sidebar({
  config,
  status,
  onClearChat,
  activityLog,
  resources,
  isResourcesVisible,
  isLoadingResources,
  isIngesting,
  chunkingMode,
  pendingReplace,
  project,
  setChunkingMode,
  setProject,
  addUrl,
  uploadFile,
  toggleResources,
  clearKnowledgeBase,
  confirmReplace,
  cancelReplace,
}: SidebarProps) {
  return (
    <aside
      className="w-80 bg-slate-900 border-r border-slate-800 flex flex-col h-[calc(100vh-56px)] overflow-hidden"
      role="complementary"
      aria-label="Sidebar"
    >
      <SettingsSection config={config} status={status} />
      <KnowledgeBasePanel
        onClearChat={onClearChat}
        activityLog={activityLog}
        resources={resources}
        isResourcesVisible={isResourcesVisible}
        isLoadingResources={isLoadingResources}
        isIngesting={isIngesting}
        chunkingMode={chunkingMode}
        pendingReplace={pendingReplace}
        project={project}
        setChunkingMode={setChunkingMode}
        setProject={setProject}
        addUrl={addUrl}
        uploadFile={uploadFile}
        toggleResources={toggleResources}
        clearKnowledgeBase={clearKnowledgeBase}
        confirmReplace={confirmReplace}
        cancelReplace={cancelReplace}
      />
    </aside>
  );
}
