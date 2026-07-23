import { useState } from 'react';
import {
  strategyResearchApi,
  type TargetOptimizationRun,
  type TargetParameterSetResult,
} from '@/api/strategyResearchApi';
import { formatNumber } from '@/components/common/utils';
import { Badge } from '@/components/common/Badge';

type Props = {
  run: TargetOptimizationRun;
  disabled?: boolean;
  onSaved?: () => void;
};

function statusTone(status: string): 'success' | 'warning' | 'neutral' | 'info' {
  if (status === 'ValidationPassed') return 'success';
  if (status === 'Overfit') return 'warning';
  if (status === 'Running') return 'info';
  return 'neutral';
}

function PassChecklist({ summary }: { summary: TargetParameterSetResult['targetPassSummary'] }) {
  const items = [
    ['Training PnL', summary.trainingPnlPassed],
    ['Validation PnL', summary.validationPnlPassed],
    ['Training PF', summary.trainingProfitFactorPassed],
    ['Validation PF', summary.validationProfitFactorPassed],
    ['Training DD', summary.trainingDrawdownPassed],
    ['Validation DD', summary.validationDrawdownPassed],
    ['Training trades', summary.trainingTradesPassed],
    ['Validation trades', summary.validationTradesPassed],
    ['Robustness', summary.robustnessPassed],
  ] as const;

  return (
    <ul className="mt-2 grid gap-1 text-sm sm:grid-cols-2">
      {items.map(([label, passed]) => (
        <li key={label} className={passed ? 'text-emerald-300' : 'text-rose-300'}>
          {passed ? '✓' : '✗'} {label}
        </li>
      ))}
    </ul>
  );
}

function ParameterSetCard({
  title,
  set,
  canApprove,
  runId,
  disabled,
  onSaved,
}: {
  title: string;
  set: TargetParameterSetResult;
  canApprove: boolean;
  runId: number;
  disabled?: boolean;
  onSaved?: () => void;
}) {
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function save(approve: boolean) {
    if (approve && !canApprove) return;
    setSaving(true);
    setError(null);
    try {
      await strategyResearchApi.saveTargetOptimizationBest(runId, { approve });
      onSaved?.();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="rounded-lg border border-slate-800 bg-slate-950/40 p-4">
      <div className="flex flex-wrap items-center gap-2">
        <h4 className="font-medium text-slate-100">{title}</h4>
        <Badge tone={statusTone(set.status)}>{set.status}</Badge>
        <span className="text-sm text-slate-400">Robustness {formatNumber(set.robustnessScore)}</span>
      </div>
      <PassChecklist summary={set.targetPassSummary} />
      {set.overfitWarnings.length > 0 ? (
        <div className="mt-3 rounded border border-amber-900/50 bg-amber-950/20 p-3 text-sm text-amber-200">
          {set.overfitWarnings.map((warning) => (
            <p key={warning}>{warning}</p>
          ))}
        </div>
      ) : null}
      {set.failReasons.length > 0 ? (
        <ul className="mt-3 list-disc pl-5 text-sm text-rose-300">
          {set.failReasons.map((reason) => (
            <li key={reason}>{reason}</li>
          ))}
        </ul>
      ) : null}
      <div className="mt-3 flex flex-wrap gap-2">
        <button
          type="button"
          disabled={disabled || saving || !canApprove}
          onClick={() => save(false)}
          className="rounded-lg border border-slate-700 px-3 py-1.5 text-sm text-slate-200 disabled:opacity-50"
        >
          Save parameter set
        </button>
        <button
          type="button"
          disabled={disabled || saving || !canApprove}
          onClick={() => save(true)}
          className="rounded-lg bg-emerald-600 px-3 py-1.5 text-sm font-medium text-white disabled:opacity-50"
        >
          Save & Approve
        </button>
      </div>
      {error ? <p className="mt-2 text-sm text-rose-300">{error}</p> : null}
      <details className="mt-3 text-sm text-slate-400">
        <summary className="cursor-pointer text-slate-300">Parameters</summary>
        <pre className="mt-2 overflow-x-auto rounded bg-slate-900 p-2 text-xs">{JSON.stringify(set.parameters, null, 2)}</pre>
      </details>
    </div>
  );
}

export function TargetOptimizationResultPanel({ run, disabled, onSaved }: Props) {
  const isRunning = run.status === 'Running';

  return (
    <div className="space-y-4">
      <div className="rounded-lg border border-slate-800 p-4">
        <div className="flex flex-wrap items-center gap-2">
          <h3 className="text-lg font-medium text-slate-100">Optimization progress</h3>
          <Badge tone={statusTone(run.status)}>{run.status}</Badge>
        </div>
        <div className="mt-3 grid gap-2 text-sm text-slate-300 sm:grid-cols-2 lg:grid-cols-4">
          <div>Completed: {run.completedCombinations} / {run.maxCombinations}</div>
          <div>Training passed: {run.trainingPassedCount}</div>
          <div>Validation passed: {run.validationPassedCount}</div>
          <div>Overfit: {run.overfitCount}</div>
          <div>Failed: {run.failedCount}</div>
          {run.heartbeatAtUtc ? <div>Heartbeat: {new Date(run.heartbeatAtUtc).toLocaleTimeString()}</div> : null}
        </div>
        {run.currentParameters && isRunning ? (
          <p className="mt-2 text-xs text-slate-500">Current: {JSON.stringify(run.currentParameters)}</p>
        ) : null}
      </div>

      {!isRunning ? (
        <>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <div className="rounded-lg border border-slate-800 p-3 text-sm">
              <div className="text-slate-400">Passed sets</div>
              <div className="text-xl text-emerald-300">{run.summary.passedCount}</div>
            </div>
            <div className="rounded-lg border border-slate-800 p-3 text-sm">
              <div className="text-slate-400">Overfit sets</div>
              <div className="text-xl text-amber-300">{run.summary.overfitCount}</div>
            </div>
            <div className="rounded-lg border border-slate-800 p-3 text-sm">
              <div className="text-slate-400">Best validation PnL</div>
              <div className="text-xl text-slate-100">{formatNumber(run.summary.bestValidationNetPnlPercent ?? 0)}%</div>
            </div>
            <div className="rounded-lg border border-slate-800 p-3 text-sm">
              <div className="text-slate-400">Best robustness</div>
              <div className="text-xl text-slate-100">{formatNumber(run.summary.bestRobustnessScore ?? 0)}</div>
            </div>
          </div>

          {run.bestPassedParameterSet ? (
            <ParameterSetCard
              title="Best passed parameter set"
              set={run.bestPassedParameterSet}
              canApprove
              runId={run.id}
              disabled={disabled}
              onSaved={onSaved}
            />
          ) : null}

          {run.bestFailedParameterSet ? (
            <ParameterSetCard
              title="Best failed / overfit parameter set"
              set={run.bestFailedParameterSet}
              canApprove={false}
              runId={run.id}
              disabled={disabled}
            />
          ) : null}

          {run.results.length > 0 ? (
            <div className="overflow-x-auto rounded-lg border border-slate-800">
              <table className="min-w-full text-left text-sm">
                <thead className="bg-slate-900/80 text-slate-400">
                  <tr>
                    <th className="px-3 py-2">Rank</th>
                    <th className="px-3 py-2">Status</th>
                    <th className="px-3 py-2">Train PnL</th>
                    <th className="px-3 py-2">Val PnL</th>
                    <th className="px-3 py-2">Train PF</th>
                    <th className="px-3 py-2">Val PF</th>
                    <th className="px-3 py-2">Val DD</th>
                    <th className="px-3 py-2">Robustness</th>
                  </tr>
                </thead>
                <tbody>
                  {run.results.slice(0, 25).map((row) => (
                    <tr key={row.rank} className="border-t border-slate-800 text-slate-300">
                      <td className="px-3 py-2">{row.rank}</td>
                      <td className="px-3 py-2">{row.status}</td>
                      <td className="px-3 py-2">{formatNumber(row.trainingMetrics?.netPnlPercent ?? 0)}%</td>
                      <td className="px-3 py-2">{formatNumber(row.validationMetrics?.netPnlPercent ?? 0)}%</td>
                      <td className="px-3 py-2">{formatNumber(row.trainingMetrics?.profitFactor ?? 0)}</td>
                      <td className="px-3 py-2">{formatNumber(row.validationMetrics?.profitFactor ?? 0)}</td>
                      <td className="px-3 py-2">{formatNumber(row.validationMetrics?.maxDrawdownPercent ?? 0)}%</td>
                      <td className="px-3 py-2">{formatNumber(row.robustnessScore)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : null}
        </>
      ) : null}
    </div>
  );
}
