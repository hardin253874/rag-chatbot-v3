import ReactMarkdown from "react-markdown";
import type { ChatMessage } from "@/types/chat";
import { SourceCitations } from "./SourceCitations";

interface BotMessageProps {
  message: ChatMessage;
}

export function BotMessage({ message }: BotMessageProps) {
  // Show status indicator while processing (no content yet)
  if (!message.content && message.status) {
    return (
      <div className="flex justify-start animate-message-enter" aria-label="Processing status">
        <div className="max-w-[85%]">
          <div className="bg-white text-slate-500 rounded-lg px-4 py-2.5 shadow text-sm flex items-center gap-2">
            <span className="inline-block w-2 h-2 bg-indigo-400 rounded-full animate-pulse" />
            <span>{message.status}</span>
          </div>
        </div>
      </div>
    );
  }

  // Don't render anything if the message has no content yet (placeholder before first chunk)
  if (!message.content) return null;

  return (
    <div className="flex justify-start animate-message-enter" aria-label="Assistant response">
      <div className="max-w-[85%] space-y-2">
        <div className="bg-white text-slate-900 rounded-lg px-4 py-2.5 shadow text-sm leading-5 prose prose-sm max-w-none">
          <ReactMarkdown>{message.content}</ReactMarkdown>
        </div>
        {message.sources && message.sources.length > 0 && (
          <SourceCitations sources={message.sources} quality={message.quality} />
        )}
      </div>
    </div>
  );
}
