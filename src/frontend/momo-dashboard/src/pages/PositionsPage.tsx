import { useState } from 'react';
import { PageHeader } from '@/components/common/PageHeader';
import { SimulationBanner } from '@/components/common/SimulationBanner';
import { LoadingState } from '@/components/common/LoadingState';
import { DataTable } from '@/components/common/DataTable';
import { formatNumber } from '@/components/common/utils';
import { useReferenceData } from '@/hooks/useReferenceData';
import { useAsync } from '@/hooks/useAsync';
import { symbolLabel } from '@/utils/referenceLookups';
import { paperTradingApi } from '@/api/paperTradingApi';

export function PositionsPage() {
  const reference = useReferenceData();
  const [selectedSessionId, setSelectedSessionId] = useState<number | null>(null);

  const sessions = useAsync(() => paperTradingApi.listSessions({ page: 1, pageSize: 20 }), []);
  const positions = useAsync(
    () => (selectedSessionId ? paperTradingApi.getPositions(selectedSessionId) : Promise.resolve([])),
    [selectedSessionId],
  );

  return (
    <div>
      <PageHeader title="Positions" description="Open simulated positions from paper sessions." />
      <SimulationBanner message="No live trading enabled. Only paper and replay positions are shown when available." />

      <div className="mb-4">
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
      </div>

      {positions.loading ? <LoadingState /> : null}

      <DataTable
        columns={[
          { key: 'symbol', header: 'Symbol', render: (row) => symbolLabel(reference.allSymbols, Number(row.symbolId), reference.exchanges) },
          { key: 'direction', header: 'Direction', render: (row) => row.direction },
          { key: 'qty', header: 'Quantity', render: (row) => formatNumber(row.quantity) },
          { key: 'entry', header: 'Entry', render: (row) => formatNumber(row.averageEntryPrice) },
          { key: 'pnl', header: 'Unrealized PnL', render: (row) => formatNumber(row.unrealizedPnl) },
          { key: 'status', header: 'Status', render: (row) => row.status },
        ]}
        rows={positions.data ?? []}
        emptyMessage="No open paper positions for the selected session."
      />
    </div>
  );
}
