import { useState } from 'react';
import { PageHeader } from '@/components/common/PageHeader';
import { LoadingState } from '@/components/common/LoadingState';
import { DataTable } from '@/components/common/DataTable';
import { formatDate, formatNumber } from '@/components/common/utils';
import { useAsync } from '@/hooks/useAsync';
import { useReferenceData } from '@/hooks/useReferenceData';
import { symbolLabel } from '@/utils/referenceLookups';
import { backtestsApi } from '@/api/backtestsApi';
import { paperTradingApi } from '@/api/paperTradingApi';
import type { BacktestTrade } from '@/api/domainTypes';

type TradeMode = 'Backtest' | 'Paper';

interface TradeRow {
  id: number;
  mode: TradeMode;
  symbolId: number;
  netPnl?: number | null;
  fees?: number | null;
  openedAtUtc: string;
  closedAtUtc?: string | null;
}

export function TradesPage() {
  const reference = useReferenceData();
  const [mode, setMode] = useState<TradeMode>('Backtest');
  const [selectedBacktestId, setSelectedBacktestId] = useState<number | null>(null);
  const [selectedSessionId, setSelectedSessionId] = useState<number | null>(null);

  const backtests = useAsync(() => backtestsApi.list({ page: 1, pageSize: 20 }), []);
  const sessions = useAsync(() => paperTradingApi.listSessions({ page: 1, pageSize: 20 }), []);
  const backtestTrades = useAsync(
    () => (selectedBacktestId ? backtestsApi.getTrades(selectedBacktestId) : Promise.resolve([] as BacktestTrade[])),
    [selectedBacktestId],
  );
  const paperTrades = useAsync(
    () => (selectedSessionId ? paperTradingApi.getTrades(selectedSessionId) : Promise.resolve([])),
    [selectedSessionId],
  );

  const rows: TradeRow[] =
    mode === 'Backtest'
      ? (backtestTrades.data ?? []).map((trade) => ({
          id: trade.id,
          mode: 'Backtest',
          symbolId: trade.symbolId,
          netPnl: trade.netPnl,
          fees: trade.fees,
          openedAtUtc: trade.openedAtUtc,
          closedAtUtc: trade.closedAtUtc,
        }))
      : (paperTrades.data ?? []).map((trade, index) => ({
          id: Number(trade.id ?? index + 1),
          mode: 'Paper',
          symbolId: Number(trade.symbolId ?? 0),
          netPnl: Number(trade.netPnl ?? 0),
          fees: Number(trade.fees ?? 0),
          openedAtUtc: String(trade.openedAtUtc ?? ''),
          closedAtUtc: trade.closedAtUtc ? String(trade.closedAtUtc) : null,
        }));

  return (
    <div>
      <PageHeader title="Trades" description="Mode-specific trade history from backtests and paper sessions." />

      <div className="mb-4 flex flex-wrap gap-3">
        <select
          value={mode}
          onChange={(event) => setMode(event.target.value as TradeMode)}
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100"
        >
          <option value="Backtest">Backtest</option>
          <option value="Paper">Paper</option>
        </select>
        {mode === 'Backtest' ? (
          <select
            value={selectedBacktestId ?? ''}
            onChange={(event) => setSelectedBacktestId(event.target.value ? Number(event.target.value) : null)}
            className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100"
          >
            <option value="">Select backtest</option>
            {backtests.data?.items.map((item) => (
              <option key={item.id} value={item.id}>
                {item.name}
              </option>
            ))}
          </select>
        ) : (
          <select
            value={selectedSessionId ?? ''}
            onChange={(event) => setSelectedSessionId(event.target.value ? Number(event.target.value) : null)}
            className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100"
          >
            <option value="">Select paper session</option>
            {sessions.data?.items.map((item) => (
              <option key={item.id} value={item.id}>
                {item.name}
              </option>
            ))}
          </select>
        )}
      </div>

      {backtestTrades.loading || paperTrades.loading ? <LoadingState /> : null}

      <DataTable
        columns={[
          { key: 'mode', header: 'Mode', render: (row) => row.mode },
          { key: 'symbol', header: 'Symbol', render: (row) => symbolLabel(reference.allSymbols, row.symbolId, reference.exchanges) },
          { key: 'pnl', header: 'Net PnL', render: (row) => formatNumber(row.netPnl) },
          { key: 'fees', header: 'Fees', render: (row) => formatNumber(row.fees) },
          { key: 'opened', header: 'Opened', render: (row) => formatDate(row.openedAtUtc) },
          { key: 'closed', header: 'Closed', render: (row) => formatDate(row.closedAtUtc) },
        ]}
        rows={rows}
      />
    </div>
  );
}
