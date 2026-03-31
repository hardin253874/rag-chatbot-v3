import type { ChatMessage } from "@/types/chat";
import { SourceCitations } from "./SourceCitations";

interface BotMessageProps {
  message: ChatMessage;
}

export function BotMessage({ message }: BotMessageProps) {
  // Don't render anything if the message has no content yet (placeholder before first chunk)
  if (!message.content) return null;

  return (
    <div className="flex justify-start animate-message-enter" aria-label="Assistant response">
      <div className="max-w-[85%] space-y-2">
        <div className="bg-white text-slate-900 rounded-lg px-4 py-2.5 shadow text-sm leading-5 whitespace-pre-wrap">
          {message.content}
        </div>
        {message.sources && message.sources.length > 0 && (
          <SourceCitations sources={message.sources} />
        )}
      </div>
    </div>
  );
}
