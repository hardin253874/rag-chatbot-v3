"use client";

import { useState, useCallback, useRef } from "react";
import type { ChatMessage, ChatHistoryEntry } from "@/types/chat";
import { streamChat } from "@/services/chatApi";

function generateId(): string {
  return `msg_${Date.now()}_${Math.random().toString(36).substring(2, 9)}`;
}

interface UseChatReturn {
  messages: ChatMessage[];
  isStreaming: boolean;
  sendMessage: (text: string) => void;
  clearMessages: () => void;
}

export function useChat(): UseChatReturn {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [isStreaming, setIsStreaming] = useState(false);
  const abortControllerRef = useRef<AbortController | null>(null);

  const sendMessage = useCallback(
    (text: string) => {
      const trimmed = text.trim();
      if (!trimmed || isStreaming) return;

      const userMessage: ChatMessage = {
        id: generateId(),
        role: "user",
        content: trimmed,
      };

      const botMessageId = generateId();
      const botPlaceholder: ChatMessage = {
        id: botMessageId,
        role: "assistant",
        content: "",
      };

      // Build history from all existing completed messages (before this new question)
      const historyForRequest: ChatHistoryEntry[] = messages.map((m) => ({
        role: m.role,
        content: m.content,
      }));

      // Add user + placeholder bot message to the UI
      setMessages((prev) => [...prev, userMessage, botPlaceholder]);
      setIsStreaming(true);

      // Create AbortController for potential cancellation
      const abortController = new AbortController();
      abortControllerRef.current = abortController;

      // Start streaming in the background
      void (async () => {
        try {
          for await (const event of streamChat(
            trimmed,
            historyForRequest,
            abortController.signal
          )) {
            if (event.type === "chunk" && event.text) {
              const chunkText = event.text;
              setMessages((prev) => {
                const updated = [...prev];
                const lastMsg = updated[updated.length - 1];
                if (lastMsg && lastMsg.role === "assistant") {
                  updated[updated.length - 1] = {
                    ...lastMsg,
                    content: lastMsg.content + chunkText,
                  };
                }
                return updated;
              });
            } else if (event.type === "sources" && event.sources) {
              const eventSources = event.sources;
              setMessages((prev) => {
                const updated = [...prev];
                const lastMsg = updated[updated.length - 1];
                if (lastMsg && lastMsg.role === "assistant") {
                  updated[updated.length - 1] = {
                    ...lastMsg,
                    sources: eventSources,
                  };
                }
                return updated;
              });
            } else if (event.type === "quality") {
              const faithfulness = event.faithfulness ?? null;
              const contextRecall = event.contextRecall ?? null;
              const warning = event.warning ?? null;
              setMessages((prev) => {
                const updated = [...prev];
                const lastMsg = updated[updated.length - 1];
                if (lastMsg && lastMsg.role === "assistant") {
                  updated[updated.length - 1] = {
                    ...lastMsg,
                    quality: { faithfulness, contextRecall, warning },
                  };
                }
                return updated;
              });
            } else if (event.type === "done") {
              setIsStreaming(false);
            }
          }

          // If stream ended without a done event, still mark as not streaming
          setIsStreaming(false);
        } catch (error: unknown) {
          // On error: update the bot placeholder with the error message
          const errorMessage =
            error instanceof Error
              ? error.message
              : "An unexpected error occurred";

          setMessages((prev) => {
            const updated = [...prev];
            const lastMsg = updated[updated.length - 1];
            if (lastMsg && lastMsg.role === "assistant") {
              updated[updated.length - 1] = {
                ...lastMsg,
                content: `Error: ${errorMessage}`,
              };
            }
            return updated;
          });
          setIsStreaming(false);
        } finally {
          abortControllerRef.current = null;
        }
      })();
    },
    [isStreaming, messages]
  );

  const clearMessages = useCallback(() => {
    // Abort any in-progress stream
    if (abortControllerRef.current) {
      abortControllerRef.current.abort();
      abortControllerRef.current = null;
    }
    setMessages([]);
    setIsStreaming(false);
  }, []);

  return { messages, isStreaming, sendMessage, clearMessages };
}
