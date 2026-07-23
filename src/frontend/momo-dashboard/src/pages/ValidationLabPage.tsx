import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { PageHeader } from '@/components/common/PageHeader';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { DataTable } from '@/components/common/DataTable';
import { Badge } from '@/components/common/Badge';
import { formatDate } from '@/components/common/utils';
import {
  validationLabApi,
  type StrategyRobustnessDecision,
  type ValidationExperiment,
  type ValidationLaboratoryReadiness,
  type ValidationLaboratoryReadinessReport,
  type ValidationRevealStatus,
} from '@/api/validationLabApi';
import { parseApiClientError } from '@/utils/apiError';

function verdictTone(verdict?: StrategyRobustnessDecision | null): 'success' | 'warning' | 'info' | 'neutral' {
  if (!verdict) return 'neutral';
  if (verdict === 'Passed' || verdict === 'ConditionallyPassed') return 'success';
  if (verdict.startsWith('FailedInsufficient')) return 'warning';
  if (verdict.startsWith('Failed') || verdict === 'Invalid') return 'warning';
  return 'info';
}

function revealTone(status: ValidationRevealStatus): 'success' | 'warning' | 'info' | 'neutral' {
  if (status === 'Revealed') return 'success';
  if (status === 'Frozen') return 'info';
  return 'neutral';
}

function readinessTone(status?: ValidationLaboratoryReadiness | null): 'success' | 'warning' | 'info' | 'neutral' {
  if (status === 'Ready') return 'success';
  if (status === 'ReadyWithWarnings') return 'warning';
  if (status === 'Blocked') return 'warning';
  return 'neutral';
}

function isLegacyMetrics(version?: string | null) {
  return version === 'ValidationMetrics/v1' || version === 'ValidationMetrics/v1.2';
}

function formatRange(from?: string | null, to?: string | null) {
  if (!from && !to) return '—';
  return `${formatDate(from)} → ${formatDate(to)}`;
}

export function ValidationLabPage() {
  const [experiments, setExperiments] = useState<ValidationExperiment[]>([]);
  const [readiness, setReadiness] = useState<ValidationLaboratoryReadinessReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = () => {
    setLoading(true);
    Promise.all([
      validationLabApi.getExperiments(50),
      validationLabApi.getReadiness().catch(() => null),
    ])
      .then(([data, readinessReport]) => {
        setExperiments(data ?? []);
        setReadiness(readinessReport);
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

  const readinessBannerClass =
    readiness?.status === 'Ready'
      ? 'border-emerald-700 bg-emerald-950/30 text-emerald-100'
      : readiness?.status === 'Blocked'
        ? 'border-rose-800 bg-rose-950/30 text-rose-100'
        : readiness?.status === 'ReadyWithWarnings'
          ? 'border-amber-800 bg-amber-950/30 text-amber-100'
          : 'border-slate-700 bg-slate-900/50 text-slate-200';

  return (
    <div>
      <PageHeader
        title="Validation Laboratory"
        description="Training-only parameter selection, frozen configuration, and unseen 70/30 holdout validation."
      />

      {readiness ? (
        <div className={`mb-4 rounded-lg border px-4 py-3 text-sm ${readinessBannerClass}`}>
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-medium">Laboratory readiness:</span>
            <Badge tone={readinessTone(readiness.status)}>{readiness.status}</Badge>
          </div>
          <p className="mt-1">{readiness.summary}</p>
          {readiness.checks?.length ? (
            <ul className="mt-2 list-disc space-y-0.5 pl-5 text-xs opacity-90">
              {readiness.checks.slice(0, 4).map((check) => (
                <li key={check.key}>
                  {check.message}
                  {check.isWarning ? ' (warning)' : ''}
                </li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : null}

      <div className="mb-4 flex items-center justify-between gap-3">
        <p className="text-sm text-slate-400">
          Determines whether a frozen strategy survives genuinely unseen historical data.
        </p>
        <Link
          to="/validation-lab/new"
          className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950 hover:bg-white"
        >
          New experiment
        </Link>
      </div>

      <DataTable
        columns={[
          {
            key: 'id',
            header: 'ID',
            render: (row: ValidationExperiment) => (
              <Link to={`/validation-lab/experiments/${row.id}`} className="text-sky-300 hover:underline">
                {row.id}
              </Link>
            ),
          },
          {
            key: 'name',
            header: 'Name',
            render: (row: ValidationExperiment) => (
              <div className="flex flex-wrap items-center gap-2">
                <Link to={`/validation-lab/experiments/${row.id}`} className="text-sky-300 hover:underline">
                  {row.name}
                </Link>
                {isLegacyMetrics(row.validationMetricsVersion) ? (
                  <Badge tone="warning">Legacy Metrics</Badge>
                ) : null}
              </div>
            ),
          },
          { key: 'strategy', header: 'Strategy', render: (row: ValidationExperiment) => row.strategyCode },
          { key: 'version', header: 'Version', render: (row: ValidationExperiment) => row.strategyVersion },
          { key: 'symbol', header: 'Symbol', render: (row: ValidationExperiment) => row.symbol },
          { key: 'timeframe', header: 'Timeframe', render: (row: ValidationExperiment) => row.timeframe },
          {
            key: 'range',
            header: 'Date Range',
            render: (row: ValidationExperiment) => formatRange(row.requestedStartUtc, row.requestedEndUtc),
          },
          {
            key: 'type',
            header: 'Experiment Type',
            render: (row: ValidationExperiment) => row.experimentType,
          },
          {
            key: 'status',
            header: 'Status',
            render: (row: ValidationExperiment) => (
              <Badge tone={row.status === 'Completed' ? 'success' : row.status === 'Failed' ? 'warning' : 'info'}>
                {row.status}
              </Badge>
            ),
          },
          {
            key: 'reveal',
            header: 'Reveal Status',
            render: (row: ValidationExperiment) => (
              <Badge tone={revealTone(row.validationRevealStatus)}>{row.validationRevealStatus}</Badge>
            ),
          },
          {
            key: 'readiness',
            header: 'Readiness',
            render: (row: ValidationExperiment) =>
              row.validationLaboratoryReadinessStatus ? (
                <Badge tone={readinessTone(row.validationLaboratoryReadinessStatus)}>
                  {row.validationLaboratoryReadinessStatus}
                </Badge>
              ) : (
                '—'
              ),
          },
          {
            key: 'verdict',
            header: 'Final Verdict',
            render: (row: ValidationExperiment) =>
              row.strategyRobustnessDecision ? (
                <Badge tone={verdictTone(row.strategyRobustnessDecision)}>{row.strategyRobustnessDecision}</Badge>
              ) : (
                '—'
              ),
          },
          {
            key: 'created',
            header: 'Created On',
            render: (row: ValidationExperiment) => formatDate(row.createdAtUtc),
          },
        ]}
        rows={experiments}
        emptyMessage="No validation experiments yet."
      />
    </div>
  );
}
