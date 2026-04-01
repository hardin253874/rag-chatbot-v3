"use client";

import { useRef } from "react";
import { MessageList } from "./MessageList";
import { ChatInput } from "./ChatInput";
import type { ChatMessage } from "@/types/chat";

interface ChatAreaProps {
  messages: ChatMessage[];
  isStreaming: boolean;
  sendMessage: (text: string) => void;
  includeHistory: boolean;
  onIncludeHistoryChange: (value: boolean) => void;
}

export function ChatArea({ messages, isStreaming, sendMessage, includeHistory, onIncludeHistoryChange }: ChatAreaProps) {
  const wasStreamingRef = useRef(false);

  // Track streaming state transitions to know when to focus
  const shouldFocusInput = wasStreamingRef.current && !isStreaming;
  wasStreamingRef.current = isStreaming;

  return (
    <div className="flex-1 flex flex-col bg-slate-50 h-[calc(100vh-56px)]">
      <MessageList messages={messages} isStreaming={isStreaming} />
      <ChatInput
        onSend={sendMessage}
        disabled={isStreaming}
        shouldFocus={shouldFocusInput}
        includeHistory={includeHistory}
        onIncludeHistoryChange={onIncludeHistoryChange}
      />
    </div>
  );
}
