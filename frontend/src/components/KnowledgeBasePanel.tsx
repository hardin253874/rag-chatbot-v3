"use client";

import { useState, useCallback } from "react";
import { useKnowledgeBase } from "@/hooks/useKnowledgeBase";
import { UrlIngest } from "./UrlIngest";
import { FileUpload } from "./FileUpload";
import { ActivityLog } from "./ActivityLog";
import { ResourceList } from "./ResourceList";
import { ConfirmDialog } from "./ConfirmDialog";

interface KnowledgeBasePanelProps {
  onClearChat: () => void;
}

export function KnowledgeBasePanel({ onClearChat }: KnowledgeBasePanelProps) {
  const {
    activityLog,
    resources,
    isResourcesVisible,
    isLoadingResources,
    isIngesting,
    addUrl,
    uploadFile,
    toggleResources,
    clearKnowledgeBase,
  } = useKnowledgeBase();

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
    </div>
  );
}
