import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { DataTable } from '@/components/common/DataTable';
import { KeyValueGrid } from '@/components/common/KeyValueGrid';
import { formatDate, formatNumber } from '@/components/common/utils';
import { strategyLabApi, type SyntheticTestResult, type StrategyHealth, type StrategyLabRun } from '@/api/strategyLabApi';
import { parseApiClientError } from '@/utils/apiError';

const LAB_STRATEGIES = new Set([
  'PRICE_STRUCTURE_BREAKOUT_RETEST',
  'PRICE_STRUCTURE_LIQUIDITY_SWEEP_RECLAIM',
]);

export function isStrategyLabStrategy(code: string) {
  return LAB_STRATEGIES.has(code);
}

export function StrategyLabRecommendationBanner() {
  return (
    <div className="mb-4 rounded-lg border border-amber-800/60 bg-amber-950/30 px-4 py-3 text-sm text-amber-100">
      Complete raw Strategy Laboratory testing before parameter optimization.
    </div>
  );
}

export function SyntheticTestsPanel({ strategyCode }: { strategyCode: string }) {
  const [results, setResults] = useState<SyntheticTestResult[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const run = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await strategyLabApi.runSyntheticTests(strategyCode);
      setResults(res ?? []);
    } catch (err) {
      setError(parseApiClientError(err).message);
    } finally {
      setLoading(false);
    }
  };

  return (
    <section className="rounded-lg border border-slate-800 p-4">
      <div className="mb-3 flex items-center justify-between">
        <h2 className="text-lg font-medium text-slate-100">Synthetic Tests</h2>
        <button type="button" onClick={run} disabled={loading} className="rounded-lg border border-slate-700 px-3 py-1.5 text-sm">
          {loading ? 'Running...' : 'Run Synthetic Tests'}
        </button>
      </div>
      {error ? <p className="mb-2 text-sm text-red-300">{error}</p> : null}
      <DataTable
        columns={[
          { key: 'scenario', header: 'Scenario', render: (row) => row.scenarioName },
          { key: 'passed', header: 'Passed', render: (row) => row.passed ? 'Yes' : 'No' },
          { key: 'expected', header: 'Expected', render: (row) => String(row.expectedCandidateCount) },
          { key: 'actual', header: 'Actual', render: (row) => String(row.actualCandidateCount) },
          { key: 'direction', header: 'Direction', render: (row) => row.actualDirection ?? '—' },
          { key: 'failure', header: 'Failure', render: (row) => row.failureDetails ?? '—' },
        ]}
        rows={results}
        emptyMessage="Run synthetic tests to verify detector logic with known candle scenarios."
      />
    </section>
  );
}

export function StrategyHealthPanel({ strategyCode }: { strategyCode: string }) {
  const [health, setHealth] = useState<StrategyHealth | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    setLoading(true);
    strategyLabApi.getStrategyHealth(strategyCode)
      .then((data) => setHealth(data ?? null))
      .finally(() => setLoading(false));
  }, [strategyCode]);

  if (loading && !health) return <p className="text-sm text-slate-400">Loading strategy health...</p>;
  if (!health) return null;

  return (
    <section className="rounded-lg border border-slate-800 p-4">
      <h2 className="mb-3 text-lg font-medium text-slate-100">Strategy Health</h2>
      <KeyValueGrid
        items={[
          { label: 'Registration', value: health.registrationStatus },
          { label: 'Required candle data', value: health.candleDataStatus },
          { label: 'Synthetic tests', value: `${health.syntheticTestsPassed}/${health.syntheticTestsTotal}` },
          { label: 'Recent evaluations', value: String(health.recentEvaluations) },
          { label: 'Recent raw candidates', value: String(health.recentRawCandidates) },
          { label: 'Candidate rate', value: `${formatNumber(health.candidateRatePer1000Candles)} per 1,000 candles` },
          { label: 'Raw trades', value: String(health.rawTrades) },
          { label: 'Confidence approval rate', value: health.confidenceApprovalRate != null ? `${formatNumber(health.confidenceApprovalRate)}%` : 'N/A' },
          { label: 'Risk approval rate', value: health.riskApprovalRate != null ? `${formatNumber(health.riskApprovalRate)}%` : 'N/A' },
          { label: 'Recent Strategy Lab runs', value: String(health.recentStrategyLabRuns) },
        ]}
      />
      {health.problemCategories.length > 0 ? (
        <p className="mt-3 text-sm text-slate-300">Problem areas: {health.problemCategories.join(', ')}</p>
      ) : null}
      {health.warnings.map((w) => <p key={w} className="mt-1 text-sm text-amber-200">{w}</p>)}
    </section>
  );
}

export function StrategyLabRunsPanel({ strategyCode }: { strategyCode: string }) {
  const [runs, setRuns] = useState<StrategyLabRun[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    strategyLabApi.getRunsByStrategy(strategyCode, 10)
      .then((data) => setRuns(data ?? []))
      .finally(() => setLoading(false));
  }, [strategyCode]);

  if (loading) return <p className="text-sm text-slate-400">Loading runs...</p>;

  return (
    <section className="rounded-lg border border-slate-800 p-4">
      <div className="mb-3 flex items-center justify-between">
        <h2 className="text-lg font-medium text-slate-100">Strategy Laboratory Runs</h2>
        <Link to="/strategy-lab" className="text-sm text-sky-300 hover:underline">New run</Link>
      </div>
      <DataTable
        columns={[
          { key: 'name', header: 'Name', render: (row) => <Link to={`/strategy-lab/runs/${row.id}`} className="text-sky-300 hover:underline">{row.name}</Link> },
          { key: 'status', header: 'Status', render: (row) => row.status },
          { key: 'fingerprint', header: 'Fingerprint', render: (row) => row.experimentFingerprint },
          { key: 'created', header: 'Created', render: (row) => formatDate(row.createdAtUtc) },
        ]}
        rows={runs}
        emptyMessage="No strategy lab runs yet."
      />
    </section>
  );
}
