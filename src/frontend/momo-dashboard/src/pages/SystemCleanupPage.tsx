import { useState } from 'react';
import { PageHeader } from '@/components/common/PageHeader';
import { EmptyState } from '@/components/common/EmptyState';
import { DataTable } from '@/components/common/DataTable';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { FormPanel } from '@/components/common/FormPanel';
import { CheckboxField, TextField } from '@/components/forms/fields';
import { formatDate } from '@/components/common/utils';
import {
  systemCleanupApi,
  CLEAN_BASELINE_CONFIRMATION,
  type CleanBaselinePreview,
  type CleanBaselineRequest,
  type CleanBaselineResult,
} from '@/api/systemCleanupApi';
import { parseApiClientError } from '@/utils/apiError';

const DEFAULT_REQUEST: CleanBaselineRequest = {
  preserveAdminUser: true,
  preserveBinanceFuturesExchange: true,
  removeStrategies: true,
  removeSymbols: true,
  removeSimulationData: true,
  removeReports: true,
  removeMarketData: true,
};

export function SystemCleanupPage() {
  const [options, setOptions] = useState<CleanBaselineRequest>(DEFAULT_REQUEST);
  const [confirmation, setConfirmation] = useState('');
  const [preview, setPreview] = useState<CleanBaselinePreview | null>(null);
  const [result, setResult] = useState<CleanBaselineResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loadingPreview, setLoadingPreview] = useState(false);
  const [executing, setExecuting] = useState(false);

  const confirmed = confirmation === CLEAN_BASELINE_CONFIRMATION;

  function updateOption(key: keyof CleanBaselineRequest, value: boolean) {
    setOptions((current) => ({ ...current, [key]: value }));
  }

  async function handlePreview() {
    setError(null);
    setResult(null);
    setLoadingPreview(true);
    try {
      const data = await systemCleanupApi.previewCleanBaseline(options);
      setPreview(data);
    } catch (err) {
      setError(parseApiClientError(err).message);
    } finally {
      setLoadingPreview(false);
    }
  }

  async function handleExecute() {
    if (!confirmed) return;
    setError(null);
    setExecuting(true);
    try {
      const data = await systemCleanupApi.executeCleanBaseline({ ...options, confirmation });
      setResult(data);
      setPreview(null);
      setConfirmation('');
    } catch (err) {
      setError(parseApiClientError(err).message);
    } finally {
      setExecuting(false);
    }
  }

  return (
    <div>
      <PageHeader
        title="System Cleanup"
        description="Reset the platform to a clean baseline. Simulation only — this never touches a real exchange account."
      />
      <ApiErrorAlert message={error} />

      <div className="mb-4 rounded-xl border border-rose-500/30 bg-rose-500/10 px-4 py-3 text-sm text-rose-200">
        This permanently deletes experimental data (symbols, strategies, simulation runs, reports). The Admin user, roles,
        settings, risk profiles, and one Binance Futures exchange are preserved. This action cannot be undone.
      </div>

      <FormPanel title="Cleanup Options" description="Choose what to remove. Defaults produce a full clean baseline.">
        <div className="grid gap-3 md:grid-cols-2">
          <CheckboxField label="Remove simulation data (backtests, benchmarks, replay, paper, decisions)" checked={options.removeSimulationData} onChange={(v) => updateOption('removeSimulationData', v)} />
          <CheckboxField label="Remove market data (candles, imports)" checked={options.removeMarketData} onChange={(v) => updateOption('removeMarketData', v)} />
          <CheckboxField label="Remove reports and run summaries" checked={options.removeReports} onChange={(v) => updateOption('removeReports', v)} />
          <CheckboxField label="Remove strategies" checked={options.removeStrategies} onChange={(v) => updateOption('removeStrategies', v)} />
          <CheckboxField label="Remove symbols" checked={options.removeSymbols} onChange={(v) => updateOption('removeSymbols', v)} />
          <CheckboxField label="Preserve one Binance Futures exchange" checked={options.preserveBinanceFuturesExchange} onChange={(v) => updateOption('preserveBinanceFuturesExchange', v)} />
        </div>
        <div className="mt-4 flex flex-wrap gap-2">
          <button
            type="button"
            onClick={() => void handlePreview()}
            disabled={loadingPreview}
            className="rounded-lg border border-slate-600 px-4 py-2 text-sm text-slate-200 hover:bg-slate-800 disabled:opacity-50"
          >
            {loadingPreview ? 'Loading preview…' : 'Preview Clean Baseline'}
          </button>
        </div>
      </FormPanel>

      {preview ? (
        <section className="mt-6 space-y-4">
          <div className="rounded-xl border border-slate-800 bg-slate-900/40 p-4">
            <h2 className="mb-3 text-sm font-medium text-slate-300">Preview (generated {formatDate(preview.generatedAtUtc)})</h2>
            <DataTable
              columns={[
                { key: 'entity', header: 'Table', render: (row) => row.entityName },
                { key: 'count', header: 'Rows', render: (row) => row.count },
                { key: 'delete', header: 'Will Delete', render: (row) => (row.willDelete ? 'Yes' : 'No') },
              ]}
              rows={preview.items}
            />
          </div>

          {preview.preserved.length > 0 ? (
            <div className="rounded-xl border border-emerald-500/30 bg-emerald-500/10 p-4 text-sm text-emerald-200">
              <p className="font-medium">Preserved:</p>
              <ul className="mt-1 list-disc pl-5">
                {preview.preserved.map((item) => (
                  <li key={item}>{item}</li>
                ))}
              </ul>
            </div>
          ) : null}

          {preview.warnings.length > 0 ? (
            <div className="rounded-xl border border-amber-500/30 bg-amber-500/10 p-4 text-sm text-amber-200">
              <ul className="list-disc pl-5">
                {preview.warnings.map((item) => (
                  <li key={item}>{item}</li>
                ))}
              </ul>
            </div>
          ) : null}

          <FormPanel
            title="Confirm & Execute"
            description={`Type ${CLEAN_BASELINE_CONFIRMATION} to enable execution.`}
          >
            <TextField
              label="Confirmation"
              value={confirmation}
              onChange={setConfirmation}
              placeholder={CLEAN_BASELINE_CONFIRMATION}
            />
            <div className="mt-4">
              <button
                type="button"
                onClick={() => void handleExecute()}
                disabled={!confirmed || executing}
                className="rounded-lg bg-rose-500/90 px-4 py-2 text-sm font-medium text-slate-950 hover:bg-rose-400 disabled:opacity-50"
              >
                {executing ? 'Executing…' : 'Execute Clean Baseline'}
              </button>
            </div>
          </FormPanel>
        </section>
      ) : null}

      {result ? (
        <section className="mt-6 space-y-4">
          <div className="rounded-xl border border-emerald-500/30 bg-emerald-500/10 p-4 text-sm text-emerald-200">
            Clean baseline completed at {formatDate(result.completedAtUtc)}. {result.binanceFuturesExchangeAction}
          </div>
          <div className="rounded-xl border border-slate-800 bg-slate-900/40 p-4">
            <h2 className="mb-3 text-sm font-medium text-slate-300">Deleted Rows</h2>
            <DataTable
              columns={[
                { key: 'entity', header: 'Table', render: (row) => row.entityName },
                { key: 'before', header: 'Before', render: (row) => row.countBefore },
                { key: 'deleted', header: 'Deleted', render: (row) => row.countDeleted },
                { key: 'after', header: 'After', render: (row) => row.countAfter },
              ]}
              rows={result.items}
            />
          </div>
        </section>
      ) : null}

      {!preview && !result ? (
        <div className="mt-6">
          <EmptyState title="No preview yet" description="Preview the clean baseline to see what will be deleted." />
        </div>
      ) : null}
    </div>
  );
}
