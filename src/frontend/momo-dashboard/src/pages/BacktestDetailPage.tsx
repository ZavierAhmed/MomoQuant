import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { PageHeader } from '@/components/common/PageHeader';
import { SimulationBanner } from '@/components/common/SimulationBanner';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { TabPanel } from '@/components/common/TabPanel';
import { PaginatedTable } from '@/components/common/PaginatedTable';
import { KeyValueGrid, formatKvDate, formatKvNumber } from '@/components/common/KeyValueGrid';
import { JsonViewerCollapsed } from '@/components/common/JsonViewerCollapsed';
import { useAsync } from '@/hooks/useAsync';
import { backtestsApi } from '@/api/backtestsApi';
import { PipelineDiagnosticsPanel } from '@/components/trading/PipelineDiagnosticsPanel';
import { BbDiagnosticChartOverlay } from '@/components/strategies/BbDiagnosticChartOverlay';

export function BacktestDetailPage() {
  const { id } = useParams();
  const backtestId = Number(id);
  const [tab, setTab] = useState('summary');

  const detail = useAsync(() => (backtestId ? backtestsApi.get(backtestId) : Promise.resolve(null)), [backtestId]);
  const results = useAsync(() => (backtestId ? backtestsApi.getResults(backtestId) : Promise.resolve(null)), [backtestId]);
  const trades = useAsync(() => (backtestId ? backtestsApi.getTrades(backtestId) : Promise.resolve([])), [backtestId]);
  const orders = useAsync(() => (backtestId ? backtestsApi.getOrders(backtestId) : Promise.resolve([])), [backtestId]);
  const missed = useAsync(() => (backtestId ? backtestsApi.getMissedOrders(backtestId) : Promise.resolve([])), [backtestId]);
  const equity = useAsync(() => (backtestId ? backtestsApi.getEquityCurve(backtestId) : Promise.resolve([])), [backtestId]);
  const strategyBreakdown = useAsync(
    () => (backtestId ? backtestsApi.getStrategyBreakdown(backtestId) : Promise.resolve([])),
    [backtestId],
  );
  const symbolBreakdown = useAsync(
    () => (backtestId ? backtestsApi.getSymbolBreakdown(backtestId) : Promise.resolve([])),
    [backtestId],
  );
  const diagnostics = useAsync(() => (backtestId ? backtestsApi.getDiagnostics(backtestId) : Promise.resolve(null)), [backtestId]);

  if (!backtestId) {
    return <ErrorState message="Invalid backtest id." />;
  }

  return (
    <div>
      <PageHeader title={detail.data?.name ?? 'Backtest Detail'} description="Backtest run details and results." />
      <Link to="/backtesting" className="mb-4 inline-block text-sm text-slate-400 underline">
        Back to backtests
      </Link>
      <SimulationBanner message="Backtesting uses historical data only. No real orders are placed." />

      {detail.loading ? <LoadingState /> : null}
      {detail.error ? <ErrorState message={detail.error} onRetry={detail.reload} /> : null}

      {detail.data ? (
        <>
          <KeyValueGrid
            items={[
              { label: 'Status', value: detail.data.status },
              { label: 'Final Balance', value: formatKvNumber(Number(detail.data.finalBalance)) },
              { label: 'Created', value: formatKvDate(detail.data.createdAtUtc) },
            ]}
          />

          <div className="mt-6">
            <TabPanel
              active={tab}
              onChange={setTab}
              tabs={[
                { id: 'summary', label: 'Summary' },
                { id: 'equity', label: 'Equity Curve' },
                { id: 'trades', label: 'Trades' },
                { id: 'orders', label: 'Orders' },
                { id: 'missed', label: 'Missed Orders' },
                { id: 'strategy', label: 'Strategy Breakdown' },
                { id: 'symbol', label: 'Symbol Breakdown' },
                { id: 'diagnostics', label: 'Pipeline Diagnostics' },
              ]}
            >
              {tab === 'summary' ? (
                <div>
                  <KeyValueGrid
                    items={[
                      { label: 'Net PnL', value: formatKvNumber(Number((results.data as Record<string, unknown> | null)?.netPnl)) },
                      { label: 'Win Rate', value: formatKvNumber(Number((results.data as Record<string, unknown> | null)?.winRate)) },
                      { label: 'Max Drawdown %', value: formatKvNumber(Number((results.data as Record<string, unknown> | null)?.maxDrawdownPercent)) },
                    ]}
                  />
                  <JsonViewerCollapsed value={results.data} />
                </div>
              ) : null}
              {tab === 'equity' ? (
                <PaginatedTable
                  rows={equity.data ?? []}
                  columns={[
                    { key: 'time', header: 'Time', render: (row) => formatKvDate(String(row.timestampUtc)) },
                    { key: 'equity', header: 'Equity', render: (row) => formatKvNumber(Number(row.equity)) },
                    { key: 'dd', header: 'Drawdown %', render: (row) => formatKvNumber(Number(row.drawdownPercent)) },
                  ]}
                />
              ) : null}
              {tab === 'trades' ? (
                <PaginatedTable
                  rows={trades.data ?? []}
                  columns={[
                    { key: 'id', header: 'ID', render: (row) => String(row.id) },
                    { key: 'pnl', header: 'Net PnL', render: (row) => formatKvNumber(Number(row.netPnl)) },
                    { key: 'opened', header: 'Opened', render: (row) => formatKvDate(String(row.openedAtUtc)) },
                  ]}
                />
              ) : null}
              {tab === 'orders' ? (
                <PaginatedTable rows={orders.data ?? []} columns={[{ key: 'id', header: 'ID', render: (row) => String(row.id) }, { key: 'status', header: 'Status', render: (row) => String(row.status) }]} />
              ) : null}
              {tab === 'missed' ? (
                <PaginatedTable rows={missed.data ?? []} columns={[{ key: 'id', header: 'ID', render: (row) => String(row.id) }, { key: 'reason', header: 'Reason', render: (row) => String(row.reason) }]} />
              ) : null}
              {tab === 'strategy' ? (
                <PaginatedTable rows={strategyBreakdown.data ?? []} columns={[{ key: 'strategy', header: 'Strategy', render: (row) => String(row.strategyName ?? row.strategyCode) }, { key: 'pnl', header: 'PnL', render: (row) => formatKvNumber(Number(row.netPnl)) }]} />
              ) : null}
              {tab === 'symbol' ? (
                <PaginatedTable rows={symbolBreakdown.data ?? []} columns={[{ key: 'symbol', header: 'Symbol', render: (row) => String(row.symbol) }, { key: 'pnl', header: 'PnL', render: (row) => formatKvNumber(Number(row.netPnl)) }]} />
              ) : null}
              {tab === 'diagnostics' ? (
                <>
                  <PipelineDiagnosticsPanel
                    diagnostics={diagnostics.data}
                    loading={diagnostics.loading}
                    error={diagnostics.error}
                  />
                  <div className="mt-4">
                    <BbDiagnosticChartOverlay diagnostics={diagnostics.data} />
                  </div>
                </>
              ) : null}
            </TabPanel>
          </div>
        </>
      ) : null}
    </div>
  );
}
