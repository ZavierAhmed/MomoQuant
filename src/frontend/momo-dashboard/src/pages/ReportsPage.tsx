import { useState } from 'react';
import { PageHeader } from '@/components/common/PageHeader';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { MetricCard } from '@/components/common/MetricCard';
import { FormPanel } from '@/components/common/FormPanel';
import { PaginatedTable } from '@/components/common/PaginatedTable';
import { KeyValueGrid, formatKvDate, formatKvNumber } from '@/components/common/KeyValueGrid';
import { JsonViewerCollapsed } from '@/components/common/JsonViewerCollapsed';
import { SelectField } from '@/components/forms/fields';
import { formatNumber } from '@/components/common/utils';
import { useAsync } from '@/hooks/useAsync';
import { reportsApi } from '@/api/reportsApi';
import { backtestsApi } from '@/api/backtestsApi';
import { paperTradingApi } from '@/api/paperTradingApi';

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' ? (value as Record<string, unknown>) : {};
}

function asArray(value: unknown): Record<string, unknown>[] {
  return Array.isArray(value) ? (value as Record<string, unknown>[]) : [];
}

function cell(value: unknown): string {
  if (value === null || value === undefined || value === '') {
    return '—';
  }
  return String(value);
}

export function ReportsPage() {
  const [selectedBacktestId, setSelectedBacktestId] = useState<number | null>(null);
  const [selectedPaperSessionId, setSelectedPaperSessionId] = useState<number | null>(null);

  const overview = useAsync(() => reportsApi.getOverview({ limit: 100 }), []);
  const strategies = useAsync(() => reportsApi.getStrategyPerformance({ limit: 50 }), []);
  const symbols = useAsync(() => reportsApi.getSymbolPerformance({ limit: 50 }), []);
  const risk = useAsync(() => reportsApi.getRiskReport({ limit: 50 }), []);
  const ai = useAsync(() => reportsApi.getAiReport({ limit: 50 }), []);
  const execution = useAsync(() => reportsApi.getExecutionReport({ limit: 50 }), []);
  const backtests = useAsync(() => backtestsApi.list({ page: 1, pageSize: 50 }), []);
  const paperSessions = useAsync(() => paperTradingApi.listSessions({ page: 1, pageSize: 50 }), []);
  const backtestReport = useAsync(
    () => (selectedBacktestId ? reportsApi.getBacktestReport(selectedBacktestId) : Promise.resolve(null)),
    [selectedBacktestId],
  );
  const paperReport = useAsync(
    () => (selectedPaperSessionId ? reportsApi.getPaperReport(selectedPaperSessionId) : Promise.resolve(null)),
    [selectedPaperSessionId],
  );

  const riskData = asRecord(risk.data);
  const aiData = asRecord(ai.data);
  const executionData = asRecord(execution.data);
  const backtestData = asRecord(backtestReport.data);
  const paperData = asRecord(paperReport.data);

  return (
    <div>
      <PageHeader title="Reports" description="Performance, risk, AI, and execution analytics." />

      {overview.loading ? <LoadingState /> : null}
      {overview.error ? <ErrorState message={overview.error} onRetry={overview.reload} /> : null}

      {overview.data ? (
        <div className="mb-6 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          <MetricCard label="Total Backtests" value={Number(overview.data.totalBacktests ?? overview.data.totalBacktestRuns ?? 0)} />
          <MetricCard label="Total Paper Sessions" value={Number(overview.data.totalPaperSessions ?? 0)} />
          <MetricCard label="Total Net PnL" value={formatNumber(Number(overview.data.totalNetPnl ?? 0))} />
          <MetricCard label="Max Drawdown %" value={formatNumber(Number(overview.data.maxDrawdownPercent ?? 0))} />
        </div>
      ) : null}

      <FormPanel title="Report Selectors" description="Choose a backtest or paper session for detailed reports.">
        <div className="grid gap-4 md:grid-cols-2">
          <SelectField
            label="Backtest Report"
            value={selectedBacktestId ?? ''}
            onChange={(value) => setSelectedBacktestId(value ? Number(value) : null)}
            options={(backtests.data?.items ?? []).map((item) => ({ label: item.name, value: item.id }))}
          />
          <SelectField
            label="Paper Session Report"
            value={selectedPaperSessionId ?? ''}
            onChange={(value) => setSelectedPaperSessionId(value ? Number(value) : null)}
            options={(paperSessions.data?.items ?? []).map((item) => ({ label: item.name, value: item.id }))}
          />
        </div>
      </FormPanel>

      <div className="grid gap-6 xl:grid-cols-2">
        <section>
          <h2 className="mb-3 text-sm font-medium text-slate-300">Strategy Performance</h2>
          <PaginatedTable
            rows={asArray(strategies.data)}
            columns={[
              { key: 'strategy', header: 'Strategy', render: (row) => cell(row.strategyName ?? row.strategyCode) },
              { key: 'trades', header: 'Trades', render: (row) => cell(row.totalTrades) },
              { key: 'winRate', header: 'Win Rate %', render: (row) => formatKvNumber(Number(row.winRatePercent)) },
              { key: 'pnl', header: 'Net PnL', render: (row) => formatKvNumber(Number(row.netPnl)) },
            ]}
          />
        </section>

        <section>
          <h2 className="mb-3 text-sm font-medium text-slate-300">Symbol Performance</h2>
          <PaginatedTable
            rows={asArray(symbols.data)}
            columns={[
              { key: 'symbol', header: 'Symbol', render: (row) => cell(row.symbol) },
              { key: 'timeframe', header: 'Timeframe', render: (row) => cell(row.timeframe) },
              { key: 'trades', header: 'Trades', render: (row) => cell(row.totalTrades) },
              { key: 'pnl', header: 'Net PnL', render: (row) => formatKvNumber(Number(row.netPnl)) },
            ]}
          />
        </section>

        <section>
          <h2 className="mb-3 text-sm font-medium text-slate-300">Risk Rejections</h2>
          <KeyValueGrid
            items={[
              { label: 'Total Decisions', value: String(riskData.totalRiskDecisions ?? '—') },
              { label: 'Approved', value: String(riskData.approvedCount ?? '—') },
              { label: 'Rejected', value: String(riskData.rejectedCount ?? '—') },
              { label: 'Rejection Rate %', value: formatKvNumber(Number(riskData.rejectionRatePercent)) },
            ]}
          />
          <div className="mt-4">
            <PaginatedTable
              rows={asArray(riskData.rejectionDetails)}
              columns={[
                { key: 'time', header: 'Time', render: (row) => formatKvDate(String(row.timestampUtc)) },
                { key: 'symbol', header: 'Symbol', render: (row) => cell(row.symbol) },
                { key: 'rule', header: 'Rule', render: (row) => cell(row.rejectedRuleKey) },
                { key: 'reason', header: 'Reason', render: (row) => cell(row.reason) },
              ]}
            />
          </div>
          <JsonViewerCollapsed value={risk.data} />
        </section>

        <section>
          <h2 className="mb-3 text-sm font-medium text-slate-300">AI Decisions</h2>
          <KeyValueGrid
            items={[
              { label: 'Total AI Decisions', value: String(aiData.totalAiDecisions ?? '—') },
              { label: 'Avg Confidence', value: formatKvNumber(Number(aiData.averageConfidenceScore)) },
              { label: 'Anomaly Count', value: String(aiData.anomalyCount ?? '—') },
              { label: 'High Confidence Losses', value: String(aiData.highConfidenceLosses ?? '—') },
            ]}
          />
          <div className="mt-4">
            <PaginatedTable
              rows={asArray(aiData.averageConfidenceByStrategy)}
              columns={[
                { key: 'strategy', header: 'Strategy', render: (row) => cell(row.strategyCode) },
                { key: 'score', header: 'Avg Confidence', render: (row) => formatKvNumber(Number(row.averageConfidenceScore)) },
              ]}
              emptyMessage="No strategy confidence data."
            />
          </div>
          <JsonViewerCollapsed value={ai.data} />
        </section>

        <section className="xl:col-span-2">
          <h2 className="mb-3 text-sm font-medium text-slate-300">Execution Report</h2>
          <KeyValueGrid
            items={[
              { label: 'Total Orders', value: String(executionData.totalOrders ?? '—') },
              { label: 'Filled Orders', value: String(executionData.filledOrders ?? '—') },
              { label: 'Missed Orders', value: String(executionData.totalMissedOrders ?? '—') },
              { label: 'Fill Rate %', value: formatKvNumber(Number(executionData.fillRatePercent)) },
            ]}
          />
          <div className="mt-4">
            <PaginatedTable
              rows={asArray(executionData.missedOrderDetails)}
              columns={[
                { key: 'symbol', header: 'Symbol', render: (row) => cell(row.symbol) },
                { key: 'strategy', header: 'Strategy', render: (row) => cell(row.strategyCode) },
                { key: 'timeframe', header: 'Timeframe', render: (row) => cell(row.timeframe) },
                { key: 'reason', header: 'Reason', render: (row) => cell(row.reason) },
              ]}
              emptyMessage="No missed order details."
            />
          </div>
          <JsonViewerCollapsed value={execution.data} />
        </section>
      </div>

      <div className="mt-6 grid gap-6 xl:grid-cols-2">
        <section className="rounded-xl border border-slate-800 bg-slate-900/40 p-4">
          <h2 className="mb-3 text-sm font-medium text-slate-300">Backtest Report Detail</h2>
          {backtestReport.loading ? <LoadingState /> : null}
          {backtestReport.data ? (
            <>
              <KeyValueGrid
                items={[
                  { label: 'Name', value: String(backtestData.name ?? '—') },
                  { label: 'Status', value: String(backtestData.status ?? '—') },
                  { label: 'Net PnL', value: formatKvNumber(Number(backtestData.netPnl)) },
                  { label: 'Win Rate %', value: formatKvNumber(Number(backtestData.winRatePercent)) },
                  { label: 'Max Drawdown %', value: formatKvNumber(Number(backtestData.maxDrawdownPercent)) },
                  { label: 'Total Trades', value: String(backtestData.totalTrades ?? '—') },
                ]}
              />
              <JsonViewerCollapsed value={backtestReport.data} />
            </>
          ) : (
            <p className="text-sm text-slate-500">Select a backtest above.</p>
          )}
        </section>

        <section className="rounded-xl border border-slate-800 bg-slate-900/40 p-4">
          <h2 className="mb-3 text-sm font-medium text-slate-300">Paper Session Report Detail</h2>
          {paperReport.loading ? <LoadingState /> : null}
          {paperReport.data ? (
            <>
              <KeyValueGrid
                items={[
                  { label: 'Name', value: String(paperData.name ?? '—') },
                  { label: 'Status', value: String(paperData.status ?? '—') },
                  { label: 'Mode', value: String(paperData.mode ?? '—') },
                  { label: 'Current Equity', value: formatKvNumber(Number(paperData.currentEquity)) },
                  { label: 'Realized PnL', value: formatKvNumber(Number(paperData.realizedPnl)) },
                  { label: 'Win Rate %', value: formatKvNumber(Number(paperData.winRatePercent)) },
                ]}
              />
              <JsonViewerCollapsed value={paperReport.data} />
            </>
          ) : (
            <p className="text-sm text-slate-500">Select a paper session above.</p>
          )}
        </section>
      </div>
    </div>
  );
}
