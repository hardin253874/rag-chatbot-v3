"use client";

import { useState, useCallback } from "react";
import type { ActivityEntry } from "@/types/activity";
import {
  ingestUrl,
  ingestFile,
  listSources,
  resetKnowledgeBase,
} from "@/services/api";

function generateEntryId(): string {
  return `entry_${Date.now()}_${Math.random().toString(36).substring(2, 9)}`;
}

function createEntry(
  type: ActivityEntry["type"],
  message: string
): ActivityEntry {
  return {
    id: generateEntryId(),
    type,
    message,
    timestamp: new Date(),
  };
}

interface UseKnowledgeBaseReturn {
  activityLog: ActivityEntry[];
  resources: string[];
  isResourcesVisible: boolean;
  isLoadingResources: boolean;
  isIngesting: boolean;
  chunkingMode: string;
  setChunkingMode: (mode: string) => void;
  addUrl: (url: string) => Promise<void>;
  uploadFile: (file: File) => Promise<void>;
  toggleResources: () => Promise<void>;
  clearKnowledgeBase: (onChatCleared: () => void) => Promise<void>;
}

export function useKnowledgeBase(): UseKnowledgeBaseReturn {
  const [activityLog, setActivityLog] = useState<ActivityEntry[]>([
    createEntry("info", "Ready. Add documents to get started."),
  ]);
  const [resources, setResources] = useState<string[]>([]);
  const [isResourcesVisible, setIsResourcesVisible] = useState(false);
  const [isLoadingResources, setIsLoadingResources] = useState(false);
  const [isIngesting, setIsIngesting] = useState(false);
  const [chunkingMode, setChunkingMode] = useState("nlp");

  const addLogEntry = useCallback(
    (type: ActivityEntry["type"], message: string) => {
      setActivityLog((prev) => [...prev, createEntry(type, message)]);
    },
    []
  );

  const addUrl = useCallback(
    async (url: string) => {
      if (!url.trim() || isIngesting) return;

      setIsIngesting(true);
      addLogEntry("info", `Ingesting ${url}...`);

      try {
        const result = await ingestUrl(url, chunkingMode);
        if (result.success) {
          addLogEntry("success", result.message);
        } else {
          addLogEntry("error", `Failed to ingest ${url}: ${result.message}`);
        }
      } catch {
        addLogEntry("error", `Failed to ingest ${url}: Network error`);
      } finally {
        setIsIngesting(false);
      }
    },
    [isIngesting, addLogEntry, chunkingMode]
  );

  const uploadFile = useCallback(
    async (file: File) => {
      if (isIngesting) return;

      setIsIngesting(true);
      addLogEntry("info", `Uploading ${file.name}...`);

      try {
        const result = await ingestFile(file, chunkingMode);
        if (result.success) {
          addLogEntry("success", result.message);
        } else {
          addLogEntry(
            "error",
            `Failed to upload ${file.name}: ${result.message}`
          );
        }
      } catch {
        addLogEntry("error", `Failed to upload ${file.name}: Network error`);
      } finally {
        setIsIngesting(false);
      }
    },
    [isIngesting, addLogEntry, chunkingMode]
  );

  const toggleResources = useCallback(async () => {
    if (isResourcesVisible) {
      setIsResourcesVisible(false);
      return;
    }

    setIsResourcesVisible(true);
    setIsLoadingResources(true);

    try {
      const result = await listSources();
      if (result.success) {
        setResources(result.sources);
      } else {
        setResources([]);
      }
    } catch {
      setResources([]);
      addLogEntry("error", "Failed to load resources");
    } finally {
      setIsLoadingResources(false);
    }
  }, [isResourcesVisible, addLogEntry]);

  const clearKnowledgeBase = useCallback(
    async (onChatCleared: () => void) => {
      setIsIngesting(true);
      addLogEntry("info", "Clearing knowledge base...");

      try {
        const result = await resetKnowledgeBase();
        if (result.success) {
          addLogEntry("success", "Knowledge base cleared");
          setResources([]);
          onChatCleared();
        } else {
          addLogEntry(
            "error",
            `Failed to clear knowledge base: ${result.message}`
          );
        }
      } catch {
        addLogEntry("error", "Failed to clear knowledge base: Network error");
      } finally {
        setIsIngesting(false);
      }
    },
    [addLogEntry]
  );

  return {
    activityLog,
    resources,
    isResourcesVisible,
    isLoadingResources,
    isIngesting,
    chunkingMode,
    setChunkingMode,
    addUrl,
    uploadFile,
    toggleResources,
    clearKnowledgeBase,
  };
}
