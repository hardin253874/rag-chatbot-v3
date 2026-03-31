export function ThinkingIndicator() {
  return (
    <div className="flex justify-start" role="status" aria-label="Generating response">
      <div className="bg-white rounded-lg px-4 py-3 shadow inline-flex items-center gap-1.5">
        <span
          className="w-1.5 h-1.5 rounded-full bg-slate-400 animate-thinking-bounce"
        />
        <span
          className="w-1.5 h-1.5 rounded-full bg-slate-400 animate-thinking-bounce"
          style={{ animationDelay: "200ms" }}
        />
        <span
          className="w-1.5 h-1.5 rounded-full bg-slate-400 animate-thinking-bounce"
          style={{ animationDelay: "400ms" }}
        />
      </div>
    </div>
  );
}
