"use client";

import { useState, useCallback } from "react";
import { Header } from "@/components/Header";
import { Sidebar } from "@/components/Sidebar";
import { ChatArea } from "@/components/ChatArea";
import { useConfig } from "@/hooks/useConfig";
import { useChat } from "@/hooks/useChat";
import { useKnowledgeBase } from "@/hooks/useKnowledgeBase";

export default function Home() {
  const { config, status } = useConfig();
  const { messages, isStreaming, includeHistory, setIncludeHistory, sendMessage, clearMessages } = useChat();
  const {
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
  } = useKnowledgeBase();

  const [chatProject, setChatProject] = useState("");

  const handleSendMessage = useCallback(
    (text: string) => {
      sendMessage(text, chatProject || undefined);
    },
    [sendMessage, chatProject]
  );

  return (
    <div className="h-screen flex flex-col overflow-hidden">
      <Header status={status} />
      <div className="flex flex-1 overflow-hidden">
        <Sidebar
          config={config}
          status={status}
          onClearChat={clearMessages}
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
        <ChatArea
          messages={messages}
          isStreaming={isStreaming}
          sendMessage={handleSendMessage}
          includeHistory={includeHistory}
          onIncludeHistoryChange={setIncludeHistory}
          projects={projects}
          selectedProject={chatProject}
          onProjectChange={setChatProject}
        />
      </div>
    </div>
  );
}
