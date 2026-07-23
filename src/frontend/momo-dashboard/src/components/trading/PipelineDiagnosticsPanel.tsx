import { KeyValueGrid, formatKvNumber } from '@/components/common/KeyValueGrid';
import { PaginatedTable } from '@/components/common/PaginatedTable';
import type { PipelineDiagnostics } from '@/api/domainTypes';

type Props = {
  diagnostics: PipelineDiagnostics | null;
  loading?: boolean;
  error?: string | null;
};

export function PipelineDiagnosticsPanel({ diagnostics, loading, error }: Props) {
  if (loading) {
    return <p className="text-sm text-slate-400">Loading pipeline diagnostics...</p>;
  }

  if (error) {
    return <p className="rounded border border-red-500/40 bg-red-500/10 p-3 text-sm text-red-200">{error}</p>;
  }

  if (!diagnostics) {
    return <p className="text-sm text-slate-400">No diagnostics available yet.</p>;
  }

  const topRejection = diagnostics.topRiskRejectionRules?.[0];
  const allRejected =
    diagnostics.entrySignals > 0 && diagnostics.riskApproved === 0 && diagnostics.riskRejected > 0;

  return (
    <div className="space-y-4">
      {allRejected && topRejection ? (
        <div className="rounded border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-100">
          All entry signals were rejected by risk. Top reason: {topRejection.ruleKey}. Average confidence was{' '}
          {formatKvNumber(Number(diagnostics.averageNormalizedConfidenceScore ?? 0))} and effective minimum was{' '}
          {formatKvNumber(Number(diagnostics.effectiveMinConfidenceScore))}.
        </div>
      ) : null}

      {diagnostics.warnings?.length ? (
        <ul className="space-y-2 rounded border border-slate-700 bg-slate-900/60 p-3 text-sm text-slate-200">
          {diagnostics.warnings.map((warning) => (
            <li key={warning}>{warning}</li>
          ))}
        </ul>
      ) : null}

      <KeyValueGrid
        items={[
          { label: 'Candles loaded', value: String(diagnostics.candleCount) },
          { label: 'Indicators available', value: String(diagnostics.indicatorSnapshotCount) },
          { label: 'Strategies evaluated', value: String(diagnostics.strategyEvaluations) },
          { label: 'Entry signals', value: String(diagnostics.entrySignals) },
          { label: 'Candidate signals', value: String(diagnostics.candidateSignals ?? diagnostics.entrySignals) },
          { label: 'NoTrade signals', value: String(diagnostics.noTradeSignals) },
          { label: 'Risk approved', value: String(diagnostics.riskApproved) },
          { label: 'Risk rejected', value: String(diagnostics.riskRejected) },
          { label: 'Orders created', value: String(diagnostics.ordersCreated) },
          { label: 'Orders filled', value: String(diagnostics.ordersFilled) },
          { label: 'Trades opened', value: String(diagnostics.tradesOpened) },
          { label: 'Trades closed', value: String(diagnostics.tradesClosed) },
          {
            label: 'Effective minimum confidence',
            value: formatKvNumber(Number(diagnostics.effectiveMinConfidenceScore)),
          },
          {
            label: 'Average confidence',
            value: formatKvNumber(Number(diagnostics.averageNormalizedConfidenceScore ?? 0)),
          },
        ]}
      />

      <PaginatedTable
        rows={diagnostics.topRiskRejectionRules ?? []}
        columns={[
          { key: 'rule', header: 'Top rejection rule', render: (row) => String(row.ruleKey) },
          { key: 'count', header: 'Count', render: (row) => String(row.count) },
        ]}
      />

      {diagnostics.bbLiquiditySweep ? (
        <div className="rounded border border-slate-800 bg-slate-900/50 p-3">
          <p className="mb-2 text-sm font-medium text-slate-200">BB Liquidity Sweep Funnel</p>
          <p className="mb-2 text-xs text-slate-400">{diagnostics.bbLiquiditySweep.pipelineSummary}</p>
          {diagnostics.bbLiquiditySweep.whyZeroTradesAnalysis ? (
            <p className="mb-2 text-xs text-amber-100">{diagnostics.bbLiquiditySweep.whyZeroTradesAnalysis}</p>
          ) : null}
          <PaginatedTable
            rows={Object.entries(diagnostics.bbLiquiditySweep.noTradeReasonBreakdown ?? {}).map(([reason, count]) => ({ reason, count }))}
            columns={[
              { key: 'reason', header: 'NoTrade reason', render: (row) => row.reason },
              { key: 'count', header: 'Count', render: (row) => String(row.count) },
            ]}
          />
        </div>
      ) : null}
    </div>
  );
}
