"use client";

import { useEffect, useRef } from "react";
import type { ChatMessage } from "@/types/chat";
import { EmptyState } from "./EmptyState";
import { UserMessage } from "./UserMessage";
import { BotMessage } from "./BotMessage";
import { ThinkingIndicator } from "./ThinkingIndicator";

interface MessageListProps {
  messages: ChatMessage[];
  isStreaming: boolean;
}

export function MessageList({ messages, isStreaming }: MessageListProps) {
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    // Auto-scroll to bottom when new messages arrive or streaming state changes
    // Only auto-scroll if user is near the bottom (within 100px threshold)
    const container = containerRef.current;
    if (!container) return;

    const isNearBottom =
      container.scrollHeight - container.scrollTop - container.clientHeight < 100;

    if (isNearBottom) {
      messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
    }
  }, [messages, isStreaming]);

  const hasMessages = messages.length > 0;

  // Show thinking indicator only while streaming AND the bot placeholder is still empty
  // Once the first chunk arrives, the streaming text itself is the visual feedback
  const lastMessage = messages[messages.length - 1];
  const showThinking =
    isStreaming &&
    (!lastMessage ||
      lastMessage.role !== "assistant" ||
      lastMessage.content === "");

  return (
    <div
      ref={containerRef}
      className="flex-1 overflow-y-auto px-6 py-6"
      role="log"
      aria-label="Chat messages"
      aria-live="polite"
    >
      <div className={`max-w-3xl mx-auto space-y-4 ${hasMessages ? "" : "h-full"}`}>
        {!hasMessages && !isStreaming ? (
          <EmptyState />
        ) : (
          <>
            {messages.map((message) =>
              message.role === "user" ? (
                <UserMessage key={message.id} message={message} />
              ) : (
                <BotMessage key={message.id} message={message} />
              )
            )}
            {showThinking && <ThinkingIndicator />}
          </>
        )}
        <div ref={messagesEndRef} />
      </div>
    </div>
  );
}
