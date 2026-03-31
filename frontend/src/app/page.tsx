"use client";

import { Header } from "@/components/Header";
import { Sidebar } from "@/components/Sidebar";
import { ChatArea } from "@/components/ChatArea";
import { useConfig } from "@/hooks/useConfig";
import { useChat } from "@/hooks/useChat";

export default function Home() {
  const { config, status } = useConfig();
  const { messages, isStreaming, sendMessage, clearMessages } = useChat();

  return (
    <div className="h-screen flex flex-col overflow-hidden">
      <Header status={status} />
      <div className="flex flex-1 overflow-hidden">
        <Sidebar config={config} status={status} onClearChat={clearMessages} />
        <ChatArea
          messages={messages}
          isStreaming={isStreaming}
          sendMessage={sendMessage}
        />
      </div>
    </div>
  );
}
