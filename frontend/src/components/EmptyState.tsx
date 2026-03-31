import { MessageSquare } from "lucide-react";

export function EmptyState() {
  return (
    <div className="flex flex-col items-center justify-center h-full text-center">
      <MessageSquare className="w-12 h-12 text-slate-300 mb-4" />
      <p className="text-sm font-medium text-slate-400 mb-1">
        Ask a question about your documents
      </p>
      <p className="text-xs text-slate-400">
        Upload documents to the knowledge base, then start a conversation.
      </p>
    </div>
  );
}
