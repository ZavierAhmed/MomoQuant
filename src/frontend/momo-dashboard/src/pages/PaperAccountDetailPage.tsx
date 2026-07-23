import { Link, useParams } from 'react-router-dom';
import { PageHeader } from '@/components/common/PageHeader';
import { SimulationBanner } from '@/components/common/SimulationBanner';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { TabPanel } from '@/components/common/TabPanel';
import { PaginatedTable } from '@/components/common/PaginatedTable';
import { KeyValueGrid, formatKvDate, formatKvNumber } from '@/components/common/KeyValueGrid';
import { useAsync } from '@/hooks/useAsync';
import { useState } from 'react';
import { paperTradingApi } from '@/api/paperTradingApi';

export function PaperAccountDetailPage() {
  const { id } = useParams();
  const accountId = Number(id);
  const [tab, setTab] = useState('summary');

  const account = useAsync(() => (accountId ? paperTradingApi.getAccount(accountId) : Promise.resolve(null)), [accountId]);
  const snapshots = useAsync(() => (accountId ? paperTradingApi.getSnapshots(accountId) : Promise.resolve([])), [accountId]);
  const sessions = useAsync(() => paperTradingApi.listSessions({ page: 1, pageSize: 100 }), []);

  if (!accountId) return <ErrorState message="Invalid paper account id." />;

  const accountSessions = (sessions.data?.items ?? []).filter((item) => item.paperAccountId === accountId);

  return (
    <div>
      <PageHeader title={account.data?.name ?? 'Paper Account'} description="Simulated account details." />
      <Link to="/paper-trading" className="mb-4 inline-block text-sm text-slate-400 underline">
        Back to paper trading
      </Link>
      <SimulationBanner message="Paper trading is simulated. No real exchange orders are placed." />

      {account.loading ? <LoadingState /> : null}
      {account.error ? <ErrorState message={account.error} onRetry={account.reload} /> : null}

      {account.data ? (
        <TabPanel
          active={tab}
          onChange={setTab}
          tabs={[
            { id: 'summary', label: 'Summary' },
            { id: 'snapshots', label: 'Snapshots' },
            { id: 'sessions', label: 'Sessions' },
          ]}
        >
          {tab === 'summary' ? (
            <KeyValueGrid
              items={[
                { label: 'Balance', value: formatKvNumber(account.data.currentBalance) },
                { label: 'Equity', value: formatKvNumber(account.data.currentEquity) },
                { label: 'Realized PnL', value: formatKvNumber(account.data.totalRealizedPnl) },
                { label: 'Fees', value: formatKvNumber(account.data.totalFees) },
                { label: 'Currency', value: account.data.currency },
                { label: 'Active', value: account.data.isActive ? 'Yes' : 'No' },
              ]}
            />
          ) : null}
          {tab === 'snapshots' ? (
            <PaginatedTable
              rows={snapshots.data ?? []}
              columns={[
                { key: 'time', header: 'Time', render: (row) => formatKvDate(String(row.timestampUtc)) },
                { key: 'equity', header: 'Equity', render: (row) => formatKvNumber(Number(row.equity)) },
                { key: 'balance', header: 'Balance', render: (row) => formatKvNumber(Number(row.balance)) },
              ]}
            />
          ) : null}
          {tab === 'sessions' ? (
            <PaginatedTable
              rows={accountSessions}
              columns={[
                { key: 'name', header: 'Name', render: (row) => row.name },
                { key: 'status', header: 'Status', render: (row) => String(row.status) },
                {
                  key: 'view',
                  header: '',
                  render: (row) => (
                    <Link to={`/paper-trading/sessions/${row.id}`} className="text-xs underline">
                      View
                    </Link>
                  ),
                },
              ]}
            />
          ) : null}
        </TabPanel>
      ) : null}
    </div>
  );
}
