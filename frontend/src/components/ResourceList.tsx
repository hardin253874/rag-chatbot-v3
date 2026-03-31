"use client";

import { ChevronDown } from "lucide-react";

interface ResourceListProps {
  resources: string[];
  isVisible: boolean;
  isLoading: boolean;
  onToggle: () => void;
}

export function ResourceList({
  resources,
  isVisible,
  isLoading,
  onToggle,
}: ResourceListProps) {
  return (
    <div>
      <button
        type="button"
        onClick={onToggle}
        className="flex items-center gap-1 text-slate-400 text-xs font-medium px-2 py-1 rounded-md transition-all duration-150 hover:text-slate-200 hover:bg-slate-800 active:bg-slate-700 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2 focus-visible:ring-offset-slate-900"
        aria-expanded={isVisible}
        aria-controls="resource-list"
      >
        List Resources
        <ChevronDown
          className={`w-3.5 h-3.5 transition-transform duration-200 ${
            isVisible ? "rotate-180" : "rotate-0"
          }`}
          aria-hidden="true"
        />
      </button>

      {isVisible && (
        <div
          id="resource-list"
          role="list"
          className="bg-slate-800/50 rounded-lg p-3 max-h-32 overflow-y-auto mt-2 scrollbar-thin scrollbar-thumb-slate-700 scrollbar-track-transparent"
        >
          {isLoading ? (
            <span className="text-xs text-slate-400 animate-pulse">
              Loading...
            </span>
          ) : resources.length === 0 ? (
            <span className="text-xs text-slate-500 italic">
              No resources found.
            </span>
          ) : (
            resources.map((source) => (
              <div
                key={source}
                role="listitem"
                className="text-xs font-mono text-slate-300 py-0.5 truncate"
                title={source}
              >
                {source}
              </div>
            ))
          )}
        </div>
      )}
    </div>
  );
}
