import { Link } from 'react-router-dom';
import { useAsync } from '@/hooks/useAsync';
import { strategyResearchApi } from '@/api/strategyResearchApi';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { Badge } from '@/components/common/Badge';
import { DataTable } from '@/components/common/DataTable';
import { formatNumber } from '@/components/common/utils';
import { timeframeLabel } from '@/constants/timeframes';

type Props = {
  strategyCode: string;
};

export function SavedParameterSetsPanel({ strategyCode }: Props) {
  const sets = useAsync(
    () => strategyResearchApi.listParameterSets({ strategyCode }),
    [strategyCode],
  );

  if (sets.loading) return <LoadingState />;
  if (sets.error) return <ErrorState message={sets.error} onRetry={sets.reload} />;

  const rows = sets.data ?? [];

  return (
    <section className="rounded-lg border border-slate-800 p-4">
      <h2 className="text-lg font-medium text-slate-100">Saved Parameter Sets</h2>
      <p className="mt-1 text-sm text-slate-400">
        Approved sets remain frozen for LivePaper. Use them in backtests or paper simulation from the backtesting page.
      </p>
      {rows.length === 0 ? (
        <p className="mt-4 text-sm text-slate-500">No saved parameter sets for this strategy yet.</p>
      ) : (
        <div className="mt-4">
          <DataTable
            columns={[
              { key: 'name', header: 'Name', render: (row) => row.name },
              {
                key: 'approved',
                header: 'Status',
                render: (row) => (
                  <Badge tone={row.isApproved ? 'success' : 'neutral'}>
                    {row.isApproved ? 'Approved' : row.source}
                  </Badge>
                ),
              },
              { key: 'timeframe', header: 'Timeframe', render: (row) => timeframeLabel(row.timeframe) },
              {
                key: 'robustness',
                header: 'Robustness',
                render: (row) => (row.robustnessScore != null ? formatNumber(row.robustnessScore) : '—'),
              },
              {
                key: 'created',
                header: 'Created',
                render: (row) => new Date(row.createdAtUtc).toLocaleDateString(),
              },
              {
                key: 'actions',
                header: 'Actions',
                render: () => (
                  <Link to="/backtesting" className="text-sm text-sky-300 hover:underline">
                    Use in backtest
                  </Link>
                ),
              },
            ]}
            rows={rows}
          />
        </div>
      )}
    </section>
  );
}
