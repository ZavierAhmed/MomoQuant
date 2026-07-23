import { useState } from 'react';
import { PageHeader } from '@/components/common/PageHeader';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { PaginatedTable } from '@/components/common/PaginatedTable';
import { FormPanel } from '@/components/common/FormPanel';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { MetricCard } from '@/components/common/MetricCard';
import { StatusPill } from '@/components/common/StatusPill';
import { KeyValueGrid, formatKvNumber } from '@/components/common/KeyValueGrid';
import { JsonViewerCollapsed } from '@/components/common/JsonViewerCollapsed';
import { AiDecisionView } from '@/components/formatters/TradingViews';
import { SymbolSelect } from '@/components/selects/EntitySelects';
import { SelectField } from '@/components/forms/fields';
import { TIMEFRAME_OPTIONS } from '@/constants/tradingOptions';
import { formatDate } from '@/components/common/utils';
import { useAsync } from '@/hooks/useAsync';
import { useReferenceData } from '@/hooks/useReferenceData';
import { useRole } from '@/hooks/useRole';
import { aiApi } from '@/api/aiApi';
import { reportsApi } from '@/api/reportsApi';
import { parseApiClientError } from '@/utils/apiError';

export function AiAnalyticsPage() {
  const { canEdit } = useRole();
  const reference = useReferenceData();
  const [actionError, setActionError] = useState<string | null>(null);
  const [diagnosticType, setDiagnosticType] = useState<'regime' | 'confidence' | 'anomaly' | 'explain'>('regime');
  const [diagnosticResult, setDiagnosticResult] = useState<Record<string, unknown> | null>(null);
  const [symbolId, setSymbolId] = useState<number | ''>('');
  const [timeframe, setTimeframe] = useState('3m');

  const health = useAsync(() => aiApi.getHealth(), []);
  const decisions = useAsync(() => aiApi.listDecisions({ page: 1, pageSize: 50 }), []);
  const report = useAsync(() => reportsApi.getAiReport({ limit: 50 }), []);

  async function runDiagnostic() {
    if (!canEdit) return;

    setActionError(null);
    try {
      const selectedSymbol = reference.allSymbols.find((item) => item.id === symbolId);
      const symbolCode = selectedSymbol?.symbol ?? 'BTCUSDT';
      const basePayload = {
        symbol: symbolCode,
        timeframe,
        strategyCode: 'EmaPullback',
        signalDirection: 'Long',
        marketRegime: 'Trending',
        strategyStrength: 75,
      };

      const result =
        diagnosticType === 'regime'
          ? await aiApi.detectRegime({ symbol: symbolCode, timeframe })
          : diagnosticType === 'confidence'
            ? await aiApi.scoreConfidence(basePayload)
            : diagnosticType === 'anomaly'
              ? await aiApi.detectAnomaly({ symbol: symbolCode, timeframe })
              : await aiApi.explainTrade({ tradeId: 1, symbol: symbolCode });

      setDiagnosticResult(result);
    } catch (error) {
      setDiagnosticResult(null);
      setActionError(parseApiClientError(error).message);
    }
  }

  const reportData = report.data && typeof report.data === 'object' ? (report.data as Record<string, unknown>) : {};

  return (
    <div>
      <PageHeader title="AI Analytics" description="AI health, decisions, and advisory diagnostics." />
      <div className="mb-4 rounded-xl border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-sm text-amber-200">
        AI is advisory only. Risk engine remains final authority.
      </div>
      <ApiErrorAlert message={actionError} />

      {canEdit ? (
        <FormPanel title="AI Diagnostics" description="Run advisory checks against sample market context.">
          <div className="grid gap-4 md:grid-cols-4">
            <SelectField
              label="Diagnostic Type"
              value={diagnosticType}
              onChange={(value) => setDiagnosticType((value as typeof diagnosticType) || 'regime')}
              options={[
                { label: 'Regime', value: 'regime' },
                { label: 'Confidence', value: 'confidence' },
                { label: 'Anomaly', value: 'anomaly' },
                { label: 'Explain Trade', value: 'explain' },
              ]}
            />
            <SymbolSelect
              label="Symbol"
              value={symbolId}
              onChange={setSymbolId}
              options={reference.allSymbolOptions}
              loading={reference.loading}
            />
            <SelectField
              label="Timeframe"
              value={timeframe}
              onChange={(value) => setTimeframe(value || '3m')}
              options={TIMEFRAME_OPTIONS}
              required
            />
            <div className="flex items-end">
              <button
                type="button"
                onClick={() => void runDiagnostic()}
                className="rounded-lg border border-slate-600 px-4 py-2 text-sm text-slate-200 hover:bg-slate-800"
              >
                Run Diagnostic
              </button>
            </div>
          </div>
          {diagnosticResult ? (
            <div className="mt-4">
              <AiDecisionView decision={diagnosticResult} />
              <JsonViewerCollapsed value={diagnosticResult} />
            </div>
          ) : null}
        </FormPanel>
      ) : null}

      {health.loading ? <LoadingState /> : null}
      {health.error ? <ErrorState message={health.error} onRetry={health.reload} /> : null}

      {health.data ? (
        <div className="mb-6 grid gap-4 md:grid-cols-3">
          <MetricCard label="Service" value={health.data.service ?? 'AI Service'} />
          <MetricCard label="Version" value={health.data.version ?? '—'} />
          <div className="rounded-xl border border-slate-800 bg-slate-900/50 p-4">
            <p className="text-xs uppercase tracking-wide text-slate-500">Status</p>
            <div className="mt-2">
              <StatusPill status={health.data.status} />
            </div>
          </div>
        </div>
      ) : null}

      <div className="mb-6">
        <h2 className="mb-3 text-sm font-medium text-slate-300">AI Decisions</h2>
        {decisions.loading ? <LoadingState /> : null}
        <PaginatedTable
          rows={decisions.data?.items ?? []}
          columns={[
            { key: 'id', header: 'ID', render: (row) => row.id },
            { key: 'score', header: 'Confidence', render: (row) => row.confidenceScore },
            { key: 'class', header: 'Classification', render: (row) => row.confidenceClassification ?? '—' },
            { key: 'allowed', header: 'Allowed', render: (row) => (row.tradeAllowed ? 'Yes' : 'No') },
            { key: 'created', header: 'Created', render: (row) => formatDate(row.createdAtUtc) },
          ]}
        />
      </div>

      <div className="mb-6">
        <h2 className="mb-3 text-sm font-medium text-slate-300">AI Report Summary</h2>
        {report.loading ? <LoadingState /> : null}
        <KeyValueGrid
          items={[
            { label: 'Total AI Decisions', value: String(reportData.totalAiDecisions ?? '—') },
            { label: 'Avg Confidence', value: formatKvNumber(Number(reportData.averageConfidenceScore)) },
            { label: 'Anomaly Count', value: String(reportData.anomalyCount ?? '—') },
            { label: 'High Confidence Losses', value: String(reportData.highConfidenceLosses ?? '—') },
          ]}
        />
        <JsonViewerCollapsed value={report.data} />
      </div>
    </div>
  );
}
