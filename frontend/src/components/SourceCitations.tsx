import { ExternalLink, FileText } from "lucide-react";

interface SourceCitationsProps {
  sources: string[];
}

function isUrl(source: string): boolean {
  return source.startsWith("http://") || source.startsWith("https://");
}

export function SourceCitations({ sources }: SourceCitationsProps) {
  if (sources.length === 0) return null;

  return (
    <div className="flex flex-wrap items-center gap-1.5 px-1">
      <span className="text-xs text-slate-400">Sources:</span>
      {sources.map((source) => {
        const url = isUrl(source);

        if (url) {
          return (
            <a
              key={source}
              href={source}
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-1 text-xs font-mono text-indigo-500
                hover:text-indigo-600 hover:underline transition-colors duration-150
                bg-indigo-50 px-2 py-0.5 rounded-md cursor-pointer"
              aria-label={`Source: ${source} (opens in new tab)`}
            >
              <ExternalLink className="w-3 h-3" aria-hidden="true" />
              {source}
            </a>
          );
        }

        return (
          <span
            key={source}
            className="inline-flex items-center gap-1 text-xs font-mono text-indigo-500
              bg-indigo-50 px-2 py-0.5 rounded-md"
          >
            <FileText className="w-3 h-3" aria-hidden="true" />
            {source}
          </span>
        );
      })}
    </div>
  );
}
