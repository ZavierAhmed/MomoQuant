import { useState } from 'react';
import { FormPanel } from '@/components/common/FormPanel';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { LoadingState } from '@/components/common/LoadingState';
import { EmptyState } from '@/components/common/EmptyState';
import { FormActions } from '@/components/forms/FormActions';
import { adminDataCleanupApi, type FakeMarketDataCleanupPreview, type FakeMarketDataCleanupResult } from '@/api/adminDataCleanupApi';
import { parseApiClientError } from '@/utils/apiError';
import { formatDate } from '@/components/common/utils';

const CONFIRMATION_TEXT = 'DELETE_FAKE_MARKET_DATA';

export function DataCleanupTab() {
  const [includeBacktests, setIncludeBacktests] = useState(true);
  const [includeReplay, setIncludeReplay] = useState(true);
  const [includePaperTrading, setIncludePaperTrading] = useState(true);
  const [includeAiDecisions, setIncludeAiDecisions] = useState(true);
  const [includeRiskDecisions, setIncludeRiskDecisions] = useState(true);
  const [includeAuditLogs, setIncludeAuditLogs] = useState(false);
  const [resetPaperAccounts, setResetPaperAccounts] = useState(false);
  const [confirmation, setConfirmation] = useState('');
  const [preview, setPreview] = useState<FakeMarketDataCleanupPreview | null>(null);
  const [result, setResult] = useState<FakeMarketDataCleanupResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  const requestBody = {
    includeBacktests,
    includeReplay,
    includePaperTrading,
    includeAiDecisions,
    includeRiskDecisions,
    includeAuditLogs,
    resetPaperAccounts,
  };

  async function handlePreview() {
    setLoading(true);
    setError(null);
    setMessage(null);
    setResult(null);
    try {
      const data = await adminDataCleanupApi.previewFakeMarketData(requestBody);
      setPreview(data);
      setMessage('Preview loaded. Review counts before executing cleanup.');
    } catch (previewError) {
      setError(parseApiClientError(previewError).message);
    } finally {
      setLoading(false);
    }
  }

  async function handleExecute() {
    setLoading(true);
    setError(null);
    setMessage(null);
    try {
      const data = await adminDataCleanupApi.executeFakeMarketData({
        ...requestBody,
        confirmation,
      });
      setResult(data);
      setPreview(null);
      setMessage('Fake market data cleanup completed.');
    } catch (executeError) {
      setError(parseApiClientError(executeError).message);
    } finally {
      setLoading(false);
    }
  }

  const canExecute = confirmation === CONFIRMATION_TEXT && !loading;

  return (
    <div className="space-y-6">
      <div className="rounded-xl border border-amber-500/30 bg-amber-500/10 p-4 text-sm text-amber-100">
        <p className="font-medium">Safety warning</p>
        <p className="mt-2">
          This removes fake/demo market data and generated simulation results. It does not remove users, strategies,
          risk profiles, exchanges, or symbols.
        </p>
      </div>

      <ApiErrorAlert message={error} />
      {message ? <p className="text-sm text-emerald-300">{message}</p> : null}

      <FormPanel title="Cleanup Options" description="Choose which generated data categories should be removed.">
        <div className="grid gap-3 md:grid-cols-2">
          <label className="flex items-center gap-2 text-sm text-slate-200">
            <input type="checkbox" checked={includeBacktests} onChange={(event) => setIncludeBacktests(event.target.checked)} />
            Include backtests
          </label>
          <label className="flex items-center gap-2 text-sm text-slate-200">
            <input type="checkbox" checked={includeReplay} onChange={(event) => setIncludeReplay(event.target.checked)} />
            Include replay
          </label>
          <label className="flex items-center gap-2 text-sm text-slate-200">
            <input type="checkbox" checked={includePaperTrading} onChange={(event) => setIncludePaperTrading(event.target.checked)} />
            Include paper trading
          </label>
          <label className="flex items-center gap-2 text-sm text-slate-200">
            <input type="checkbox" checked={includeAiDecisions} onChange={(event) => setIncludeAiDecisions(event.target.checked)} />
            Include AI decisions
          </label>
          <label className="flex items-center gap-2 text-sm text-slate-200">
            <input type="checkbox" checked={includeRiskDecisions} onChange={(event) => setIncludeRiskDecisions(event.target.checked)} />
            Include risk decisions
          </label>
          <label className="flex items-center gap-2 text-sm text-slate-200">
            <input type="checkbox" checked={includeAuditLogs} onChange={(event) => setIncludeAuditLogs(event.target.checked)} />
            Include audit logs
          </label>
          <label className="flex items-center gap-2 text-sm text-slate-200">
            <input type="checkbox" checked={resetPaperAccounts} onChange={(event) => setResetPaperAccounts(event.target.checked)} />
            Reset paper accounts
          </label>
        </div>

        <div className="mt-4">
          <label className="mb-1 block text-sm text-slate-300" htmlFor="cleanup-confirmation">
            Confirmation (type {CONFIRMATION_TEXT})
          </label>
          <input
            id="cleanup-confirmation"
            type="text"
            value={confirmation}
            onChange={(event) => setConfirmation(event.target.value)}
            className="w-full rounded-lg border border-slate-700 bg-slate-900 px-3 py-2 text-sm text-slate-100"
            placeholder={CONFIRMATION_TEXT}
          />
        </div>

        <FormActions>
          <button type="button" onClick={() => void handlePreview()} disabled={loading} className="rounded-lg border border-slate-600 px-4 py-2 text-sm text-slate-200">
            Preview Cleanup
          </button>
          <button
            type="button"
            onClick={() => void handleExecute()}
            disabled={!canExecute}
            className="rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white disabled:cursor-not-allowed disabled:opacity-50"
          >
            Execute Cleanup
          </button>
        </FormActions>
      </FormPanel>

      {loading ? <LoadingState /> : null}

      {preview ? (
        <FormPanel title="Preview Counts" description={`Generated at ${formatDate(preview.generatedAtUtc)}`}>
          {preview.warnings.length > 0 ? (
            <ul className="mb-4 list-disc pl-5 text-sm text-amber-200">
              {preview.warnings.map((warning) => (
                <li key={warning}>{warning}</li>
              ))}
            </ul>
          ) : null}
          <div className="overflow-x-auto">
            <table className="min-w-full text-left text-sm text-slate-200">
              <thead className="border-b border-slate-700 text-slate-400">
                <tr>
                  <th className="px-3 py-2">Entity</th>
                  <th className="px-3 py-2">Count</th>
                  <th className="px-3 py-2">Will Delete</th>
                </tr>
              </thead>
              <tbody>
                {preview.items.map((item) => (
                  <tr key={item.entityName} className="border-b border-slate-800">
                    <td className="px-3 py-2">{item.entityName}</td>
                    <td className="px-3 py-2">{item.count}</td>
                    <td className="px-3 py-2">{item.willDelete ? 'Yes' : 'No'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </FormPanel>
      ) : null}

      {result ? (
        <FormPanel title="Cleanup Result" description={`Completed at ${formatDate(result.completedAtUtc)}`}>
          <div className="overflow-x-auto">
            <table className="min-w-full text-left text-sm text-slate-200">
              <thead className="border-b border-slate-700 text-slate-400">
                <tr>
                  <th className="px-3 py-2">Entity</th>
                  <th className="px-3 py-2">Before</th>
                  <th className="px-3 py-2">Deleted</th>
                  <th className="px-3 py-2">After</th>
                </tr>
              </thead>
              <tbody>
                {result.items.map((item) => (
                  <tr key={item.entityName} className="border-b border-slate-800">
                    <td className="px-3 py-2">{item.entityName}</td>
                    <td className="px-3 py-2">{item.countBefore}</td>
                    <td className="px-3 py-2">{item.countDeleted}</td>
                    <td className="px-3 py-2">{item.countAfter}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </FormPanel>
      ) : null}

      {!preview && !result && !loading ? (
        <EmptyState title="No preview yet" description="Run a preview to see what would be deleted before executing cleanup." />
      ) : null}
    </div>
  );
}
