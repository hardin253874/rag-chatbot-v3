"use client";

import { useState, useCallback, useEffect } from "react";
import type { ActivityEntry, IngestSseEvent } from "@/types/activity";
import {
  ingestUrl,
  ingestFile,
  listSources,
  resetKnowledgeBase,
  getProjects,
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

interface PendingReplace {
  file?: File;
  url?: string;
  source: string;
}

interface UseKnowledgeBaseReturn {
  activityLog: ActivityEntry[];
  resources: string[];
  isResourcesVisible: boolean;
  isLoadingResources: boolean;
  isIngesting: boolean;
  chunkingMode: string;
  pendingReplace: PendingReplace | null;
  project: string;
  projects: string[];
  setChunkingMode: (mode: string) => void;
  setProject: (project: string) => void;
  addUrl: (url: string) => Promise<void>;
  uploadFile: (file: File) => Promise<void>;
  toggleResources: () => Promise<void>;
  clearKnowledgeBase: (onChatCleared: () => void) => Promise<void>;
  confirmReplace: () => Promise<void>;
  cancelReplace: () => void;
  refreshProjects: () => Promise<void>;
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
  const [project, setProject] = useState("NESA");
  const [projects, setProjects] = useState<string[]>([]);
  const [pendingReplace, setPendingReplace] = useState<PendingReplace | null>(
    null
  );

  const addLogEntry = useCallback(
    (type: ActivityEntry["type"], message: string) => {
      setActivityLog((prev) => [...prev, createEntry(type, message)]);
    },
    []
  );

  const refreshProjects = useCallback(async () => {
    try {
      const projectList = await getProjects();
      setProjects(projectList);
    } catch {
      // Silently fail — projects list is non-critical
    }
  }, []);

  // Load projects on mount
  useEffect(() => {
    void refreshProjects();
  }, [refreshProjects]);

  const handleSseEvent = useCallback(
    (event: IngestSseEvent) => {
      switch (event.type) {
        case "status":
          addLogEntry("info", event.message);
          break;
        case "done":
          addLogEntry("success", event.message);
          void refreshProjects();
          break;
        case "error":
          addLogEntry("error", event.message);
          break;
      }
    },
    [addLogEntry, refreshProjects]
  );

  const addUrl = useCallback(
    async (url: string) => {
      if (!url.trim() || isIngesting) return;

      setIsIngesting(true);
      addLogEntry("info", `Ingesting ${url}...`);

      try {
        const preCheck = await ingestUrl(
          url,
          chunkingMode,
          handleSseEvent,
          false,
          project
        );

        if (preCheck) {
          if (preCheck.status === "duplicate") {
            addLogEntry("info", preCheck.message);
          } else if (preCheck.status === "exists") {
            setPendingReplace({ url, source: preCheck.source || url });
          }
        }
      } catch (err) {
        const message =
          err instanceof Error ? err.message : "Network error";
        addLogEntry("error", `Failed to ingest ${url}: ${message}`);
      } finally {
        setIsIngesting(false);
      }
    },
    [isIngesting, addLogEntry, chunkingMode, handleSseEvent, project]
  );

  const uploadFile = useCallback(
    async (file: File) => {
      if (isIngesting) return;

      setIsIngesting(true);
      addLogEntry("info", `Uploading ${file.name}...`);

      try {
        const preCheck = await ingestFile(
          file,
          chunkingMode,
          handleSseEvent,
          false,
          project
        );

        if (preCheck) {
          if (preCheck.status === "duplicate") {
            addLogEntry("info", preCheck.message);
          } else if (preCheck.status === "exists") {
            setPendingReplace({
              file,
              source: preCheck.source || file.name,
            });
          }
        }
      } catch (err) {
        const message =
          err instanceof Error ? err.message : "Network error";
        addLogEntry(
          "error",
          `Failed to upload ${file.name}: ${message}`
        );
      } finally {
        setIsIngesting(false);
      }
    },
    [isIngesting, addLogEntry, chunkingMode, handleSseEvent, project]
  );

  const confirmReplace = useCallback(async () => {
    if (!pendingReplace) return;

    const { file, url, source } = pendingReplace;
    setPendingReplace(null);
    setIsIngesting(true);
    addLogEntry("info", `Replacing ${source}...`);

    try {
      if (file) {
        await ingestFile(file, chunkingMode, handleSseEvent, true, project);
      } else if (url) {
        await ingestUrl(url, chunkingMode, handleSseEvent, true, project);
      }
    } catch (err) {
      const message =
        err instanceof Error ? err.message : "Network error";
      addLogEntry("error", `Failed to replace ${source}: ${message}`);
    } finally {
      setIsIngesting(false);
    }
  }, [pendingReplace, addLogEntry, chunkingMode, handleSseEvent, project]);

  const cancelReplace = useCallback(() => {
    setPendingReplace(null);
    addLogEntry("info", "Upload cancelled");
  }, [addLogEntry]);

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
    pendingReplace,
    project,
    projects,
    setChunkingMode,
    setProject,
    addUrl,
    uploadFile,
    toggleResources,
    clearKnowledgeBase,
    confirmReplace,
    cancelReplace,
    refreshProjects,
  };
}
