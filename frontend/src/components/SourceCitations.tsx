import { ExternalLink, FileText } from "lucide-react";

interface QualityScores {
  faithfulness: number | null;
  contextRecall: number | null;
  warning?: string | null;
}

interface SourceCitationsProps {
  sources: string[];
  quality?: QualityScores;
}

function isUrl(source: string): boolean {
  return source.startsWith("http://") || source.startsWith("https://");
}

function scoreColor(score: number): string {
  if (score >= 0.8) return "text-green-600";
  if (score >= 0.5) return "text-yellow-600";
  return "text-red-500";
}

function formatPercent(score: number | null): string {
  if (score === null) return "--";
  return `${Math.round(score * 100)}%`;
}

export function SourceCitations({ sources, quality }: SourceCitationsProps) {
  if (sources.length === 0) return null;

  const showQuality =
    quality !== undefined &&
    (quality.faithfulness !== null || quality.contextRecall !== null);

  return (
    <div className="space-y-1 px-1">
      <div className="flex flex-wrap items-center gap-1.5">
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

      {showQuality && (
        <div className="flex items-center gap-1.5 text-xs text-slate-400">
          <span>Quality:</span>
          {quality!.faithfulness !== null && (
            <span className={scoreColor(quality!.faithfulness)}>
              Faithfulness {formatPercent(quality!.faithfulness)}
            </span>
          )}
          {quality!.faithfulness !== null && quality!.contextRecall !== null && (
            <span className="text-slate-500">&middot;</span>
          )}
          {quality!.contextRecall !== null && (
            <span className={scoreColor(quality!.contextRecall)}>
              Context Recall {formatPercent(quality!.contextRecall)}
            </span>
          )}
        </div>
      )}
      {quality?.warning && (
        <div className="flex items-center gap-1 text-xs text-amber-600 mt-0.5">
          <span>&#9888;</span>
          <span>{quality.warning}</span>
        </div>
      )}
    </div>
  );
}
