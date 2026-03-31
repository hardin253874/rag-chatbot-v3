"use client";

import { useState, useCallback } from "react";

interface UrlIngestProps {
  onAddUrl: (url: string) => Promise<void>;
  disabled: boolean;
}

export function UrlIngest({ onAddUrl, disabled }: UrlIngestProps) {
  const [url, setUrl] = useState("");

  const handleSubmit = useCallback(async () => {
    const trimmed = url.trim();
    if (!trimmed || disabled) return;

    await onAddUrl(trimmed);
    setUrl("");
  }, [url, disabled, onAddUrl]);

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key === "Enter") {
        e.preventDefault();
        void handleSubmit();
      }
    },
    [handleSubmit]
  );

  return (
    <div className="space-y-2">
      <label
        htmlFor="url-input"
        className="text-xs font-medium text-slate-300 block"
      >
        Add URL
      </label>
      <input
        id="url-input"
        type="url"
        value={url}
        onChange={(e) => setUrl(e.target.value)}
        onKeyDown={handleKeyDown}
        placeholder="https://example.com/page"
        disabled={disabled}
        aria-label="URL to ingest"
        className="w-full bg-slate-800 border border-slate-700 rounded-md px-3 py-2 text-sm text-slate-100 placeholder:text-slate-500 transition-colors duration-150 hover:border-slate-600 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 disabled:bg-slate-800/50 disabled:text-slate-600 disabled:cursor-not-allowed"
      />
      <button
        type="button"
        onClick={() => void handleSubmit()}
        disabled={disabled || !url.trim()}
        className="w-full bg-slate-800 text-slate-200 text-xs font-medium px-3 py-1.5 rounded-md border border-slate-700 transition-all duration-150 hover:bg-slate-700 hover:border-slate-600 hover:text-white active:bg-slate-600 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2 focus-visible:ring-offset-slate-900 disabled:bg-slate-800/50 disabled:text-slate-600 disabled:border-slate-700/50 disabled:cursor-not-allowed"
        aria-label="Add URL"
      >
        {disabled ? "Adding..." : "Add URL"}
      </button>
    </div>
  );
}
