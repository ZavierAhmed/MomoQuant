import { useState } from 'react';
import { createExport, getExportDownloadUrl, type ExportJobDto } from '@/api/exportsApi';

export interface ExportActionsProps {
  scope: string;
  sourceId: string;
  defaultFileName?: string;
  availableFormats?: Array<'json' | 'pdf'>;
  allowFullDetail?: boolean;
  includeChartsOption?: boolean;
  includeDiagnosticsOption?: boolean;
  includeTradesOption?: boolean;
  includeRawOption?: boolean;
}

export function ExportActions({
  scope,
  sourceId,
  availableFormats = ['json', 'pdf'],
  allowFullDetail = true,
  includeDiagnosticsOption = true,
  includeTradesOption = true,
  includeRawOption = true,
}: ExportActionsProps) {
  const [job, setJob] = useState<ExportJobDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [includeDiagnostics, setIncludeDiagnostics] = useState(true);
  const [includeTrades, setIncludeTrades] = useState(true);
  const [includeRaw, setIncludeRaw] = useState(true);

  const runExport = async (format: 'json' | 'pdf') => {
    setLoading(true);
    setError(null);
    try {
      const result = await createExport({
        scope,
        sourceId,
        format,
        detailLevel: allowFullDetail ? 'full' : 'summary',
        includeDiagnostics: includeDiagnosticsOption ? includeDiagnostics : false,
        includeTrades: includeTradesOption ? includeTrades : false,
        includeRawJson: includeRawOption ? includeRaw : false,
        includeSettings: true,
        includeRejectedCandidates: true,
      });
      setJob(result);
    } catch (exportError) {
      setError(exportError instanceof Error ? exportError.message : 'Export failed.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="flex flex-col gap-2 rounded-lg border border-slate-700 bg-slate-900/60 p-3">
      <div className="text-sm font-medium text-slate-200">Export</div>
      <div className="flex flex-wrap gap-2">
        {availableFormats.includes('pdf') && (
          <button
            type="button"
            className="rounded bg-slate-700 px-3 py-1.5 text-sm text-white hover:bg-slate-600 disabled:opacity-50"
            disabled={loading}
            onClick={() => void runExport('pdf')}
          >
            Export summary PDF
          </button>
        )}
        {availableFormats.includes('json') && (
          <button
            type="button"
            className="rounded bg-indigo-700 px-3 py-1.5 text-sm text-white hover:bg-indigo-600 disabled:opacity-50"
            disabled={loading}
            onClick={() => void runExport('json')}
          >
            Export full data JSON
          </button>
        )}
      </div>
      {(includeDiagnosticsOption || includeTradesOption || includeRawOption) && (
        <div className="flex flex-wrap gap-3 text-xs text-slate-300">
          {includeDiagnosticsOption && (
            <label className="flex items-center gap-1">
              <input
                type="checkbox"
                checked={includeDiagnostics}
                onChange={(event) => setIncludeDiagnostics(event.target.checked)}
              />
              Include diagnostics
            </label>
          )}
          {includeTradesOption && (
            <label className="flex items-center gap-1">
              <input type="checkbox" checked={includeTrades} onChange={(event) => setIncludeTrades(event.target.checked)} />
              Include trades
            </label>
          )}
          {includeRawOption && (
            <label className="flex items-center gap-1">
              <input type="checkbox" checked={includeRaw} onChange={(event) => setIncludeRaw(event.target.checked)} />
              Include raw calculations
            </label>
          )}
        </div>
      )}
      {loading && <div className="text-xs text-slate-400">Preparing export...</div>}
      {error && <div className="text-xs text-rose-400">{error}</div>}
      {job?.status === 'Completed' && job.exportId && (
        <a
          className="text-xs text-indigo-300 hover:text-indigo-200"
          href={getExportDownloadUrl(job.exportId)}
          target="_blank"
          rel="noreferrer"
        >
          Download {job.fileName ?? 'export'}
        </a>
      )}
    </div>
  );
}
