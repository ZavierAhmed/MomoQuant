import { useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { PageHeader } from '@/components/common/PageHeader';
import { SimulationBanner } from '@/components/common/SimulationBanner';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { Badge } from '@/components/common/Badge';
import { DataTable } from '@/components/common/DataTable';
import { KeyValueGrid } from '@/components/common/KeyValueGrid';
import { TargetOptimizationLab } from '@/components/strategies/TargetOptimizationLab';
import {
  isStrategyLabStrategy,
  StrategyHealthPanel,
  StrategyLabRecommendationBanner,
  StrategyLabRunsPanel,
  SyntheticTestsPanel,
} from '@/components/strategies/StrategyLabStrategyPanels';
import { SavedParameterSetsPanel } from '@/components/strategies/SavedParameterSetsPanel';
import { useAsync } from '@/hooks/useAsync';
import { strategiesApi } from '@/api/strategiesApi';
import { timeframeLabel } from '@/constants/timeframes';

type DetailTab = 'overview' | 'parameters' | 'synthetic' | 'health' | 'lab-runs' | 'validation' | 'optimization' | 'saved-sets';

const BASE_TAB_OPTIONS: { id: DetailTab; label: string }[] = [
  { id: 'overview', label: 'Overview' },
  { id: 'parameters', label: 'Parameters' },
  { id: 'validation', label: 'Validation' },
  { id: 'optimization', label: 'Optimization Lab' },
  { id: 'saved-sets', label: 'Saved Parameter Sets' },
];

const LAB_TAB_OPTIONS: { id: DetailTab; label: string }[] = [
  { id: 'overview', label: 'Overview' },
  { id: 'parameters', label: 'Parameters' },
  { id: 'synthetic', label: 'Synthetic Tests' },
  { id: 'health', label: 'Strategy Health' },
  { id: 'lab-runs', label: 'Strategy Laboratory Runs' },
  { id: 'validation', label: 'Validation' },
  { id: 'optimization', label: 'Optimization Lab' },
  { id: 'saved-sets', label: 'Saved Parameter Sets' },
];

export function StrategyDetailPage() {
  const { strategyCode = '' } = useParams();
  const navigate = useNavigate();
  const [activeTab, setActiveTab] = useState<DetailTab>('overview');
  const detail = useAsync(() => strategiesApi.getByCode(strategyCode), [strategyCode]);

  const parameterRows = useMemo(
    () =>
      (detail.data?.parameterDefinitions ?? []).map((definition) => ({
        key: definition.key,
        label: definition.label,
        defaultValue: definition.defaultValue,
        minValue: definition.minValue ?? '—',
        maxValue: definition.maxValue ?? '—',
        step: definition.step ?? '—',
        optimizable: definition.isOptimizable ? 'Yes' : 'No',
        description: definition.description ?? '—',
      })),
    [detail.data?.parameterDefinitions],
  );

  if (detail.loading) return <LoadingState />;
  if (detail.error || !detail.data) {
    return <ErrorState message={detail.error ?? 'Strategy not found.'} onRetry={detail.reload} />;
  }

  const strategy = detail.data;
  const tabOptions = isStrategyLabStrategy(strategy.code) ? LAB_TAB_OPTIONS : BASE_TAB_OPTIONS;

  return (
    <div>
      <div className="mb-6 flex items-start justify-between gap-4">
        <PageHeader title={strategy.name} description={strategy.code} />
        <button
          type="button"
          onClick={() => navigate(-1)}
          className="rounded-lg border border-slate-700 px-3 py-1.5 text-sm text-slate-200"
        >
          Back
        </button>
      </div>
      <SimulationBanner message="Strategy details are for research and simulation only. Live trading remains disabled." />

      {strategy.researchStatus === 'Failed' ? (
        <div className="mb-4 rounded-lg border border-rose-800 bg-rose-950/40 px-4 py-3 text-sm text-rose-100">
          <div className="font-medium">Research Failed</div>
          <div>Not eligible for Deployment Qualification.</div>
          {strategy.canonicalValidationExperimentId ? (
            <div className="mt-1">
              Canonical evidence:{' '}
              <Link
                to={`/validation-lab/experiments/${strategy.canonicalValidationExperimentId}`}
                className="underline"
              >
                Validation Experiment {strategy.canonicalValidationExperimentId}
              </Link>
            </div>
          ) : null}
        </div>
      ) : null}

      <div className="mb-4 flex flex-wrap gap-2">
        {strategy.category ? <Badge tone="neutral">{strategy.category}</Badge> : null}
        <Badge tone="neutral">{strategy.isBuiltIn ? 'Built-in' : 'Custom'}</Badge>
        <Badge tone={strategy.isEnabled ? 'success' : 'warning'}>{strategy.isEnabled ? 'Enabled' : 'Disabled'}</Badge>
        {(strategy.supportedModes ?? []).map((mode) => (
          <Badge key={mode} tone="info">{mode}</Badge>
        ))}
      </div>

      {isStrategyLabStrategy(strategy.code) ? <StrategyLabRecommendationBanner /> : null}

      <div className="mb-6 flex flex-wrap gap-2 border-b border-slate-800 pb-3">
        {tabOptions.map((tab) => (
          <button
            key={tab.id}
            type="button"
            onClick={() => setActiveTab(tab.id)}
            className={`rounded-lg px-3 py-1.5 text-sm ${
              activeTab === tab.id
                ? 'bg-slate-100 font-medium text-slate-950'
                : 'border border-slate-700 text-slate-300'
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {activeTab === 'overview' ? (
        <>
          <section className="mb-6 rounded-lg border border-slate-800 p-4">
            <h2 className="text-lg font-medium text-slate-100">How this strategy works</h2>
            <p className="mt-2 text-sm text-slate-300">{strategy.howItWorks}</p>
            <KeyValueGrid
              items={[
                { label: 'Entry logic', value: strategy.entryLogic ?? '—' },
                { label: 'Exit logic', value: strategy.exitLogic ?? '—' },
                { label: 'When it avoids trading', value: strategy.noTradeConditions ?? '—' },
                { label: 'Risk / trade management', value: strategy.riskManagement ?? '—' },
              ]}
            />
          </section>

          <section className="mb-6 rounded-lg border border-slate-800 p-4">
            <h2 className="text-lg font-medium text-slate-100">Timeframes</h2>
            <div className="mt-3">
              <KeyValueGrid
                items={[
                  { label: 'Preferred execution', value: strategy.preferredTimeframe ? timeframeLabel(strategy.preferredTimeframe) : '—' },
                  { label: 'Allowed execution', value: (strategy.allowedTimeframes ?? []).map(timeframeLabel).join(', ') || '—' },
                  { label: 'Required data', value: (strategy.requiredDataTimeframes ?? []).map(timeframeLabel).join(', ') || '—' },
                  { label: 'Anchor timeframes', value: (strategy.anchorTimeframes ?? []).map(timeframeLabel).join(', ') || '—' },
                  { label: 'Warmup candles', value: String(strategy.warmupCandles ?? '—') },
                ]}
              />
            </div>
          </section>

          <section className="mb-6 rounded-lg border border-slate-800 p-4">
            <h2 className="text-lg font-medium text-slate-100">Indicators</h2>
            <p className="mt-2 text-sm text-slate-300">
              {(strategy.requiredIndicators ?? []).join(', ') || 'No required indicators listed.'}
            </p>
          </section>

          <section className="mb-6 rounded-lg border border-slate-800 p-4">
            <h2 className="text-lg font-medium text-slate-100">Validation & optimization</h2>
            <div className="mt-3">
              <KeyValueGrid
                items={[
                  { label: 'Supports validation', value: strategy.supportsValidation ? 'Yes' : 'No' },
                  { label: 'Supports optimization', value: strategy.supportsOptimization ? 'Yes' : 'No' },
                  { label: 'Recommended validation mode', value: strategy.recommendedValidationMode ?? '—' },
                  { label: 'Guardrails', value: (strategy.optimizationGuardrails ?? []).join(' ') || '—' },
                ]}
              />
            </div>
          </section>

          <section className="mb-6 rounded-lg border border-slate-800 p-4">
            <h2 className="text-lg font-medium text-slate-100">Approximation / source notes</h2>
            <p className="mt-2 text-sm text-slate-300">{strategy.approximationNotes ?? 'No approximation notes.'}</p>
            {strategy.implementationNotes ? (
              <p className="mt-2 text-sm text-slate-400">{strategy.implementationNotes}</p>
            ) : null}
          </section>

          <section className="rounded-lg border border-slate-800 p-4">
            <h2 className="text-lg font-medium text-slate-100">Actions</h2>
            <div className="mt-3 flex flex-wrap gap-2">
              <Link to="/backtesting" className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950">
                Run Backtest
              </Link>
              <Link to="/strategy-benchmarks" className="rounded-lg border border-slate-700 px-4 py-2 text-sm text-slate-200">
                Run Benchmark
              </Link>
              <button
                type="button"
                onClick={() => setActiveTab('validation')}
                className="rounded-lg border border-slate-700 px-4 py-2 text-sm text-slate-200"
              >
                Validate current parameters
              </button>
              {strategy.supportsOptimization ? (
                <button
                  type="button"
                  onClick={() => setActiveTab('optimization')}
                  className="rounded-lg border border-emerald-800 px-4 py-2 text-sm text-emerald-200"
                >
                  Find better parameters
                </button>
              ) : null}
            </div>
          </section>
        </>
      ) : null}

      {activeTab === 'parameters' ? (
        <section className="rounded-lg border border-slate-800 p-4">
          <h2 className="text-lg font-medium text-slate-100">Parameters</h2>
          {parameterRows.length > 0 ? (
            <DataTable
              columns={[
                { key: 'key', header: 'Key', render: (row) => row.key },
                { key: 'label', header: 'Label', render: (row) => row.label },
                { key: 'defaultValue', header: 'Default', render: (row) => row.defaultValue },
                { key: 'minValue', header: 'Min', render: (row) => row.minValue },
                { key: 'maxValue', header: 'Max', render: (row) => row.maxValue },
                { key: 'step', header: 'Step', render: (row) => row.step },
                { key: 'optimizable', header: 'Optimizable', render: (row) => row.optimizable },
                { key: 'description', header: 'Description', render: (row) => row.description },
              ]}
              rows={parameterRows}
            />
          ) : (
            <p className="mt-2 text-sm text-slate-400">No structured parameter definitions are available for this strategy.</p>
          )}
          {strategy.supportsOptimization ? (
            <p className="mt-3 text-sm text-emerald-300">Can be optimized using 70/30 validation in the Optimization Lab.</p>
          ) : null}
        </section>
      ) : null}

      {activeTab === 'validation' ? (
        <div className="rounded-lg border border-slate-800 p-4 text-sm text-slate-400">
          <p className="mb-4 text-slate-300">
            Use the Optimization Lab tab to configure exchange, symbol, timeframe, and date range. The validation section there runs your current parameters through 70/30 validation without searching new parameter sets.
          </p>
          <button
            type="button"
            onClick={() => setActiveTab('optimization')}
            className="rounded-lg border border-slate-700 px-4 py-2 text-sm text-slate-200"
          >
            Open Optimization Lab
          </button>
        </div>
      ) : null}

      {activeTab === 'optimization' ? <TargetOptimizationLab strategy={strategy} /> : null}

      {activeTab === 'synthetic' && isStrategyLabStrategy(strategy.code) ? (
        <SyntheticTestsPanel strategyCode={strategy.code} />
      ) : null}

      {activeTab === 'health' && isStrategyLabStrategy(strategy.code) ? (
        <StrategyHealthPanel strategyCode={strategy.code} />
      ) : null}

      {activeTab === 'lab-runs' && isStrategyLabStrategy(strategy.code) ? (
        <StrategyLabRunsPanel strategyCode={strategy.code} />
      ) : null}

      {activeTab === 'saved-sets' ? <SavedParameterSetsPanel strategyCode={strategy.code} /> : null}
    </div>
  );
}
