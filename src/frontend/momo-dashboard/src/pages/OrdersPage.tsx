import { useState } from 'react';
import { PageHeader } from '@/components/common/PageHeader';
import { LoadingState } from '@/components/common/LoadingState';
import { DataTable } from '@/components/common/DataTable';
import { formatDate } from '@/components/common/utils';
import { useReferenceData } from '@/hooks/useReferenceData';
import { useAsync } from '@/hooks/useAsync';
import { symbolLabel } from '@/utils/referenceLookups';
import { backtestsApi } from '@/api/backtestsApi';
import { paperTradingApi } from '@/api/paperTradingApi';

type OrderMode = 'Backtest' | 'Paper';

export function OrdersPage() {
  const reference = useReferenceData();
  const [mode, setMode] = useState<OrderMode>('Backtest');
  const [selectedBacktestId, setSelectedBacktestId] = useState<number | null>(null);
  const [selectedSessionId, setSelectedSessionId] = useState<number | null>(null);

  const backtests = useAsync(() => backtestsApi.list({ page: 1, pageSize: 20 }), []);
  const sessions = useAsync(() => paperTradingApi.listSessions({ page: 1, pageSize: 20 }), []);
  const backtestOrders = useAsync(
    () => (selectedBacktestId ? backtestsApi.getOrders(selectedBacktestId) : Promise.resolve([])),
    [selectedBacktestId],
  );
  const paperOrders = useAsync(
    () => (selectedSessionId ? paperTradingApi.getOrders(selectedSessionId) : Promise.resolve([])),
    [selectedSessionId],
  );

  const rows = mode === 'Backtest' ? backtestOrders.data ?? [] : paperOrders.data ?? [];

  return (
    <div>
      <PageHeader title="Orders" description="Simulated orders from backtests and paper sessions." />

      <div className="mb-4 flex flex-wrap gap-3">
        <select
          value={mode}
          onChange={(event) => setMode(event.target.value as OrderMode)}
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

      {backtestOrders.loading || paperOrders.loading ? <LoadingState /> : null}

      <DataTable
        columns={[
          { key: 'id', header: 'ID', render: (row) => String(row.id ?? '—') },
          { key: 'status', header: 'Status', render: (row) => String(row.status ?? '—') },
          { key: 'symbol', header: 'Symbol', render: (row) => symbolLabel(reference.allSymbols, Number(row.symbolId), reference.exchanges) },
          { key: 'created', header: 'Created', render: (row) => formatDate(String(row.requestedAtUtc ?? row.createdAtUtc ?? '')) },
        ]}
        rows={rows}
      />
    </div>
  );
}
