"use client";

import { useEffect, useRef } from "react";
import type { ActivityEntry } from "@/types/activity";

interface ActivityLogProps {
  entries: ActivityEntry[];
}

const bulletClass: Record<ActivityEntry["type"], string> = {
  info: "bg-blue-400",
  success: "bg-green-400",
  error: "bg-red-400",
};

const textClass: Record<ActivityEntry["type"], string> = {
  info: "text-blue-300",
  success: "text-green-300",
  error: "text-red-300",
};

export function ActivityLog({ entries }: ActivityLogProps) {
  const logEndRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    logEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [entries.length]);

  return (
    <div className="space-y-1">
      <span className="text-xs font-medium text-slate-300 mb-1 block">
        Activity
      </span>
      <div
        className="bg-slate-800/50 rounded-lg p-3 max-h-40 overflow-y-auto space-y-1 scrollbar-thin scrollbar-thumb-slate-700 scrollbar-track-transparent"
        role="log"
        aria-label="Activity log"
        aria-live="polite"
      >
        {entries.map((entry) => (
          <div
            key={entry.id}
            className="flex items-start gap-2 text-xs leading-4"
          >
            <span
              className={`w-1.5 h-1.5 rounded-full mt-[5px] flex-shrink-0 ${bulletClass[entry.type]}`}
              aria-hidden="true"
            />
            <span className={`font-mono break-words ${textClass[entry.type]}`}>
              {entry.message}
            </span>
          </div>
        ))}
        <div ref={logEndRef} />
      </div>
    </div>
  );
}
