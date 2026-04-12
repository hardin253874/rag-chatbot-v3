"use client";

import { useState, useCallback } from "react";
import type { ActivityEntry } from "@/types/activity";
import { UrlIngest } from "./UrlIngest";
import { FileUpload } from "./FileUpload";
import { ActivityLog } from "./ActivityLog";
import { ResourceList } from "./ResourceList";
import { ConfirmDialog } from "./ConfirmDialog";

interface PendingReplace {
  file?: File;
  url?: string;
  source: string;
}

interface KnowledgeBasePanelProps {
  onClearChat: () => void;
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

export function KnowledgeBasePanel({
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
}: KnowledgeBasePanelProps) {
  const [isConfirmOpen, setIsConfirmOpen] = useState(false);

  const handleClearClick = useCallback(() => {
    setIsConfirmOpen(true);
  }, []);

  const handleConfirmClear = useCallback(async () => {
    setIsConfirmOpen(false);
    await clearKnowledgeBase(onClearChat);
  }, [clearKnowledgeBase, onClearChat]);

  const handleCancelClear = useCallback(() => {
    setIsConfirmOpen(false);
  }, []);

  return (
    <div className="border-t border-slate-700 flex-1 flex flex-col overflow-hidden px-5 py-4">
      <h2 className="text-xs font-semibold uppercase tracking-wider text-slate-400 mb-3 flex-shrink-0">
        Knowledge Base
      </h2>

      <div className="flex-1 overflow-y-auto space-y-4 scrollbar-thin scrollbar-thumb-slate-700 scrollbar-track-transparent">
        <div className="space-y-1.5">
          <label
            htmlFor="chunking-mode-select"
            className="text-xs font-medium text-slate-300 block"
          >
            Chunking Mode
          </label>
          <select
            id="chunking-mode-select"
            value={chunkingMode}
            onChange={(e) => setChunkingMode(e.target.value)}
            disabled={isIngesting}
            className="w-full bg-slate-800 border border-slate-700 rounded-md px-3 py-2 text-sm text-slate-100 transition-colors duration-150 hover:border-slate-600 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 disabled:bg-slate-800/50 disabled:text-slate-600 disabled:cursor-not-allowed"
            aria-label="Select chunking mode for document ingestion"
          >
            <option value="fixed">Fixed</option>
            <option value="nlp">NLP Dynamic</option>
            <option value="smart">LLM Smart</option>
            <option value="hybrid">Hybrid (NLP + LLM)</option>
          </select>
        </div>

        <div className="space-y-1.5">
          <label
            htmlFor="project-input"
            className="text-xs font-medium text-slate-300 block"
          >
            Project
          </label>
          <input
            id="project-input"
            type="text"
            value={project}
            onChange={(e) => setProject(e.target.value)}
            placeholder="e.g. NESA"
            disabled={isIngesting}
            className="w-full bg-slate-800 border border-slate-700 rounded-md px-3 py-2 text-sm text-slate-100 transition-colors duration-150 hover:border-slate-600 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 disabled:bg-slate-800/50 disabled:text-slate-600 disabled:cursor-not-allowed placeholder:text-slate-500"
            aria-label="Project name for document tagging"
          />
        </div>

        <UrlIngest onAddUrl={addUrl} disabled={isIngesting} />

        <FileUpload onUploadFile={uploadFile} disabled={isIngesting} />

        <ActivityLog entries={activityLog} />

        <ResourceList
          resources={resources}
          isVisible={isResourcesVisible}
          isLoading={isLoadingResources}
          onToggle={toggleResources}
        />

        <button
          type="button"
          onClick={handleClearClick}
          disabled={isIngesting}
          className="w-full mt-4 bg-transparent text-red-400 text-xs font-medium px-3 py-1.5 rounded-md border border-red-500/30 transition-all duration-150 hover:bg-red-500/10 hover:border-red-500/50 hover:text-red-300 active:bg-red-500/20 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-red-500 focus-visible:ring-offset-2 focus-visible:ring-offset-slate-900 disabled:opacity-50 disabled:cursor-not-allowed"
          aria-label="Clear knowledge base"
        >
          Clear Knowledge Base
        </button>
      </div>

      <ConfirmDialog
        isOpen={isConfirmOpen}
        title="Clear Knowledge Base?"
        message="This will permanently delete all documents and embeddings. Chat history will also be cleared. This action cannot be undone."
        confirmLabel="Yes, Clear Everything"
        onConfirm={() => void handleConfirmClear()}
        onCancel={handleCancelClear}
      />

      <ConfirmDialog
        isOpen={!!pendingReplace}
        title="Replace Document?"
        message={`${pendingReplace?.source ?? ""} already exists in the knowledge base. Replace with the new version?`}
        confirmLabel="Yes, Replace"
        onConfirm={() => void confirmReplace()}
        onCancel={cancelReplace}
      />
    </div>
  );
}
