"use client";

import { useState, useCallback, useRef, useEffect } from "react";
import { ArrowUp } from "lucide-react";

interface ChatInputProps {
  onSend: (text: string) => void;
  disabled: boolean;
  shouldFocus: boolean;
}

export function ChatInput({ onSend, disabled, shouldFocus }: ChatInputProps) {
  const [value, setValue] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);

  const trimmedValue = value.trim();
  const canSend = trimmedValue.length > 0 && !disabled;

  const handleSend = useCallback(() => {
    if (!canSend) return;
    onSend(trimmedValue);
    setValue("");
  }, [canSend, onSend, trimmedValue]);

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key === "Enter" && !e.shiftKey) {
        e.preventDefault();
        handleSend();
      }
    },
    [handleSend]
  );

  // Focus the input when shouldFocus transitions to true (streaming ended)
  useEffect(() => {
    if (shouldFocus && !disabled) {
      inputRef.current?.focus();
    }
  }, [shouldFocus, disabled]);

  return (
    <div className="px-6 py-4 border-t border-slate-200 bg-white">
      <div className="max-w-3xl mx-auto flex items-center gap-3">
        <div className="flex-1">
          <input
            ref={inputRef}
            type="text"
            value={value}
            onChange={(e) => setValue(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Ask a question..."
            disabled={disabled}
            aria-label="Message input"
            className="w-full bg-white border border-slate-300 rounded-md px-4 py-2.5
              text-sm text-slate-900 placeholder:text-slate-400
              transition-colors duration-150
              hover:border-slate-400
              focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500
              disabled:bg-slate-100 disabled:text-slate-400 disabled:cursor-not-allowed"
          />
        </div>
        <button
          type="button"
          onClick={handleSend}
          disabled={!canSend}
          aria-label="Send message"
          className="bg-indigo-500 text-white w-9 h-9 rounded-md flex items-center justify-center
            shadow-sm transition-all duration-150
            hover:bg-indigo-600
            active:bg-indigo-700
            focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2
            disabled:bg-slate-200 disabled:text-slate-400 disabled:cursor-not-allowed disabled:shadow-none"
        >
          <ArrowUp className="w-4 h-4" />
        </button>
      </div>
    </div>
  );
}
