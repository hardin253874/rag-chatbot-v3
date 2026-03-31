import type { ChatMessage } from "@/types/chat";

interface UserMessageProps {
  message: ChatMessage;
}

export function UserMessage({ message }: UserMessageProps) {
  return (
    <div className="flex justify-end animate-message-enter" aria-label="You said">
      <div className="max-w-[85%] bg-indigo-50 text-slate-900 rounded-lg px-4 py-2.5 text-sm leading-5">
        {message.content}
      </div>
    </div>
  );
}
