import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { PageHeader } from '@/components/common/PageHeader';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { DataTable } from '@/components/common/DataTable';
import { formatDate } from '@/components/common/utils';
import { strategyLabApi, type StrategyLabRun } from '@/api/strategyLabApi';
import { parseApiClientError } from '@/utils/apiError';

export function StrategyLabRunsPage() {
  const [runs, setRuns] = useState<StrategyLabRun[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = () => {
    setLoading(true);
    strategyLabApi.getRuns(50)
      .then((data) => {
        setRuns(data ?? []);
        setError(null);
      })
      .catch((err: unknown) => setError(parseApiClientError(err).message))
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    load();
  }, []);

  if (loading) return <LoadingState />;
  if (error) return <ErrorState message={error} onRetry={load} />;

  return (
    <div>
      <PageHeader title="Strategy Laboratory Runs" description="Recent research runs for price-structure strategies." />
      <div className="mb-4">
        <Link to="/strategy-lab" className="text-sm text-sky-300 hover:underline">New Strategy Lab run</Link>
      </div>
      <DataTable
        columns={[
          { key: 'name', header: 'Name', render: (row) => <Link to={`/strategy-lab/runs/${row.id}`} className="text-sky-300 hover:underline">{row.name}</Link> },
          { key: 'strategy', header: 'Strategy', render: (row) => row.strategyCode },
          { key: 'symbol', header: 'Symbol', render: (row) => `${row.symbol} ${row.timeframe}` },
          { key: 'status', header: 'Status', render: (row) => row.status },
          { key: 'fingerprint', header: 'Fingerprint', render: (row) => row.experimentFingerprint },
          { key: 'created', header: 'Created', render: (row) => formatDate(row.createdAtUtc) },
        ]}
        rows={runs}
        emptyMessage="No strategy lab runs yet."
      />
    </div>
  );
}
