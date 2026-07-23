import { useState, type ReactNode } from 'react';
import {
  strategyResearchApi,
  type ParameterOptimizationResult,
  type ParameterSetResult,
  type StrategyFunnelDiagnostics,
  type StrategyValidationResult,
  type ZeroTradeAnalysis,
} from '@/api/strategyResearchApi';
import { buildDefaultParameterSetName } from '@/utils/strategyTimeframes';

type SaveContext = {
  strategyCode: string;
  symbolId: number;
  timeframe: string;
  symbolLabel: string;
  optimizationRunId?: number;
  trainingRange?: { fromUtc: string; toUtc: string };
  validationRange?: { fromUtc: string; toUtc: string };
};

type QuickActionHandlers = {
  onRunBacktest?: () => void;
  onRunExploratory?: () => void;
  onRunOptimization?: () => void;
  onExtendDateRange?: () => void;
  onViewDiagnostics?: () => void;
};

type Props = {
  validationResult?: StrategyValidationResult | null;
  optimizationResult?: ParameterOptimizationResult | null;
  saveContext?: SaveContext;
  disabled?: boolean;
  onSaved?: (parameterSetId: number, approved: boolean) => void;
  onUseInBacktest?: (parameterSetId: number) => void;
  onUseInLivePaper?: (parameterSetId: number) => void;
  quickActions?: QuickActionHandlers;
};

export function OptimizationResultPanel({
  validationResult,
  optimizationResult,
  saveContext,
  disabled,
  onSaved,
  onUseInBacktest,
  onUseInLivePaper,
  quickActions,
}: Props) {
  const [savingRank, setSavingRank] = useState<number | null>(null);
  const [savedIds, setSavedIds] = useState<Record<number, number>>({});
  const [error, setError] = useState<string | null>(null);

  if (!validationResult && !optimizationResult) {
    return null;
  }

  async function saveSet(set: ParameterSetResult, approve: boolean) {
    if (!saveContext) return;
    const validationTradeCount = set.validationMetrics?.tradeCount ?? 0;
    if (approve && (set.passStatus === 'Failed' || validationTradeCount === 0)) {
      setError('This parameter set cannot be approved.');
      return;
    }

    setSavingRank(set.rank);
    setError(null);
    try {
      const response = await strategyResearchApi.saveParameterSet({
        name: buildDefaultParameterSetName(saveContext.strategyCode, saveContext.symbolLabel, saveContext.timeframe),
        strategyCode: saveContext.strategyCode,
        symbolId: saveContext.symbolId,
        timeframe: saveContext.timeframe,
        parameters: set.parameters,
        optimizationRunId: saveContext.optimizationRunId ?? optimizationResult?.optimizationRunId,
        trainingRange: saveContext.trainingRange,
        validationRange: saveContext.validationRange,
        trainingMetrics: set.trainingMetrics,
        validationMetrics: set.validationMetrics,
        robustnessScore: set.robustnessScore,
        approve,
        validationStatus: set.passStatus,
        validationTradeCount,
      });
      setSavedIds((current) => ({ ...current, [set.rank]: response.id }));
      onSaved?.(response.id, approve);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to save parameter set.');
    } finally {
      setSavingRank(null);
    }
  }

  return (
    <div className="mt-6 space-y-4">
      {error ? <p className="text-sm text-red-400">{error}</p> : null}

      {validationResult ? (
        <ResultSection title={`Validation — ${validationResult.validationStatus}`}>
          <p className="text-slate-400">
            Robustness score: {validationResult.robustnessScore} · Engine: {validationResult.backtestEngineUsed ?? 'BacktestEngine'}
          </p>
          {validationResult.isExploratoryProfile ? (
            <p className="mt-1 text-amber-300">Exploratory profile — research only, not final validation.</p>
          ) : null}
          <div className="mt-3 grid gap-4 md:grid-cols-2">
            <MetricsBlock title="Training" metrics={validationResult.trainingMetrics} />
            <MetricsBlock title="Validation (out-of-sample)" metrics={validationResult.validationMetrics} />
          </div>
          <DataCoverageBlock result={validationResult} />
          <FunnelBlock title="Training pipeline" funnel={validationResult.trainingFunnel} />
          <FunnelBlock title="Validation pipeline" funnel={validationResult.validationFunnel} />
          <WhyZeroTradesCard
            analysis={validationResult.whyZeroTrades ?? validationResult.validationWhyZeroTrades}
            engineBug={validationResult.engineEvaluationBug}
          />
          <StatusLists failReasons={validationResult.failReasons} warnings={validationResult.warnings} />
          <QuickActions result={validationResult} handlers={quickActions} />
          {saveContext ? (
            <ValidationSaveActions
              validationResult={validationResult}
              saveContext={saveContext}
              disabled={disabled}
              onSaved={onSaved}
              onUseInBacktest={onUseInBacktest}
              onUseInLivePaper={onUseInLivePaper}
            />
          ) : null}
        </ResultSection>
      ) : null}

      {optimizationResult ? (
        <ResultSection
          title={`Optimization — ${optimizationResult.status}`}
          subtitle={`${optimizationResult.completedCombinations}/${optimizationResult.totalCombinations} combinations · ${optimizationResult.tradeProducingParameterSetCount ?? 0} produced trades · ${optimizationResult.zeroTradeParameterSetCount ?? 0} zero-trade`}
        >
          {optimizationResult.bestParameterSets.map((set) => (
            <ParameterSetRow
              key={set.rank}
              set={set}
              savedId={savedIds[set.rank]}
              saving={savingRank === set.rank}
              disabled={disabled || !saveContext}
              onSave={() => void saveSet(set, false)}
              onSaveApprove={() => void saveSet(set, true)}
              onUseInBacktest={onUseInBacktest}
              onUseInLivePaper={onUseInLivePaper}
            />
          ))}
          {optimizationResult.warnings.length > 0 ? (
            <ul className="mt-2 list-disc pl-5 text-amber-300">
              {optimizationResult.warnings.map((warning) => (
                <li key={warning}>{warning}</li>
              ))}
            </ul>
          ) : null}
        </ResultSection>
      ) : null}
    </div>
  );
}

function DataCoverageBlock({ result }: { result: StrategyValidationResult }) {
  if (!result.candleCoverage?.length && !result.trainingCandleCount && !result.validationCandleCount) {
    return null;
  }

  return (
    <div className="mt-4 rounded border border-slate-800 p-3">
      <p className="font-medium text-slate-200">Data coverage</p>
      {result.importedDuringRun ? (
        <p className="mt-1 text-xs text-emerald-300">Missing candles were imported automatically before this run.</p>
      ) : null}
      <ul className="mt-1 space-y-0.5 text-slate-400">
        <li>Training evaluation candles: {result.trainingEvaluationCandles ?? result.trainingCandleCount ?? '—'}</li>
        <li>Validation evaluation candles: {result.validationEvaluationCandles ?? result.validationCandleCount ?? '—'}</li>
        <li>Training warmup loaded: {result.trainingWarmupCandlesLoaded ?? '—'}</li>
        <li>Validation warmup loaded: {result.validationWarmupCandlesLoaded ?? '—'}</li>
        <li>Training evaluations: {result.trainingEvaluations ?? result.trainingFunnel?.evaluations ?? '—'}</li>
        <li>Validation evaluations: {result.validationEvaluations ?? result.validationFunnel?.evaluations ?? '—'}</li>
        {result.candleCoverage?.map((item) => (
          <li key={item.timeframe}>
            {item.timeframe}: {item.candleCount} candles ({item.coverageStatus})
            {item.importedDuringRun ? ' · imported during run' : ''}
            {item.importError ? ` · ${item.importError}` : ''}
          </li>
        ))}
      </ul>
    </div>
  );
}

function FunnelBlock({ title, funnel }: { title: string; funnel?: StrategyFunnelDiagnostics }) {
  if (!funnel) return null;
  return (
    <div className="mt-4 rounded border border-slate-800 p-3">
      <p className="font-medium text-slate-200">{title}</p>
      <p className="mt-1 text-xs text-slate-400">{funnel.pipelineSummary}</p>
      <ul className="mt-2 grid gap-1 text-xs text-slate-400 md:grid-cols-2">
        <li>Evaluations: {funnel.evaluations}</li>
        <li>Volatility passed: {funnel.volatilityGatePassedCount}</li>
        <li>Momentum passed: {funnel.momentumPassedCount}</li>
        <li>Retest: {funnel.retestDetectedCount}</li>
        <li>Confirmation: {funnel.confirmationDetectedCount}</li>
        <li>Candidates: {funnel.candidateSignals}</li>
        <li>Risk rejected: {funnel.riskRejectedCount}</li>
        <li>Trades: {funnel.tradesCreated}</li>
      </ul>
    </div>
  );
}

function WhyZeroTradesCard({ analysis, engineBug }: { analysis?: ZeroTradeAnalysis; engineBug?: boolean }) {
  if (!analysis && !engineBug) return null;
  const isEngineBug = engineBug || analysis?.reasonCode === 'EngineEvaluationBug';
  return (
    <div className={`mt-4 rounded border p-3 ${isEngineBug ? 'border-red-900/60 bg-red-950/20' : 'border-amber-900/60 bg-amber-950/20'}`}>
      <p className={`font-medium ${isEngineBug ? 'text-red-200' : 'text-amber-200'}`}>
        {isEngineBug ? 'Engine diagnostics issue' : 'Why no trades?'}
      </p>
      {analysis ? (
        <>
          <p className={`mt-1 text-sm ${isEngineBug ? 'text-red-100' : 'text-amber-100'}`}>{analysis.mostLikelyBlocker}</p>
          <p className="mt-2 text-sm text-slate-300">{analysis.explanation}</p>
          <p className="mt-2 text-sm text-slate-400">Next: {analysis.suggestedNextAction}</p>
          {analysis.relatedParameter ? (
            <p className="mt-1 text-xs text-slate-500">Related parameter: {analysis.relatedParameter}</p>
          ) : null}
        </>
      ) : (
        <p className="mt-2 text-sm text-red-100">
          Candles are available, but the strategy engine did not evaluate them. This is an engine/diagnostics issue, not a market no-trade result.
        </p>
      )}
    </div>
  );
}

function QuickActions({
  result,
  handlers,
}: {
  result: StrategyValidationResult;
  handlers?: QuickActionHandlers;
}) {
  const zeroTrades = (result.validationMetrics?.tradeCount ?? 0) === 0;
  const failed = result.validationStatus === 'Failed';
  if (!zeroTrades && !failed) return null;
  if (!handlers) return null;

  return (
    <div className="mt-4 flex flex-wrap gap-2">
      {handlers.onRunBacktest ? <ActionButton label="Run normal backtest" onClick={handlers.onRunBacktest} /> : null}
      {handlers.onRunExploratory ? <ActionButton label="Run Exploratory profile" onClick={handlers.onRunExploratory} /> : null}
      {handlers.onRunOptimization ? <ActionButton label="Run optimization" onClick={handlers.onRunOptimization} /> : null}
      {handlers.onExtendDateRange ? <ActionButton label="Extend date range" onClick={handlers.onExtendDateRange} /> : null}
      {handlers.onViewDiagnostics ? <ActionButton label="View diagnostics" onClick={handlers.onViewDiagnostics} /> : null}
    </div>
  );
}

function ResultSection({
  title,
  subtitle,
  children,
}: {
  title: string;
  subtitle?: string;
  children: ReactNode;
}) {
  return (
    <div className="rounded-lg border border-slate-700 bg-slate-900/50 p-4 text-sm">
      <h4 className="font-medium text-slate-100">{title}</h4>
      {subtitle ? <p className="mt-1 text-slate-400">{subtitle}</p> : null}
      <div className="mt-3">{children}</div>
    </div>
  );
}

function ParameterSetRow({
  set,
  savedId,
  saving,
  disabled,
  onSave,
  onSaveApprove,
  onUseInBacktest,
  onUseInLivePaper,
}: {
  set: ParameterSetResult;
  savedId?: number;
  saving: boolean;
  disabled?: boolean;
  onSave: () => void;
  onSaveApprove: () => void;
  onUseInBacktest?: (parameterSetId: number) => void;
  onUseInLivePaper?: (parameterSetId: number) => void;
}) {
  const canApprove = set.passStatus !== 'Failed' && (set.validationMetrics?.tradeCount ?? 0) > 0;

  return (
    <div className="mt-3 rounded border border-slate-800 p-3">
      <p className="font-medium">
        #{set.rank} — Score {set.optimizationScore} — {set.passStatus}
      </p>
      <p className="text-slate-400">Robustness: {set.robustnessScore}</p>
      <p className="mt-1 text-xs text-slate-500">
        Parameters: {Object.entries(set.parameters).map(([key, value]) => `${key}=${value}`).join(', ') || '—'}
      </p>
      <div className="mt-2 grid gap-3 md:grid-cols-2">
        <MetricsBlock title="Training" metrics={set.trainingMetrics} />
        <MetricsBlock title="Validation" metrics={set.validationMetrics} />
      </div>
      <StatusLists failReasons={set.failReasons} warnings={set.warnings} />
      <div className="mt-3 flex flex-wrap gap-2">
        <ActionButton label={saving ? 'Saving…' : 'Save'} onClick={onSave} disabled={disabled || saving} />
        <ActionButton
          label={saving ? 'Saving…' : 'Save & Approve'}
          onClick={onSaveApprove}
          disabled={disabled || saving || !canApprove}
        />
        {!canApprove ? (
          <span className="self-center text-xs text-amber-300">Cannot approve failed or zero-trade set.</span>
        ) : null}
        {savedId ? (
          <>
            <ActionButton label="Use in new backtest" onClick={() => onUseInBacktest?.(savedId)} />
            <ActionButton label="Use in LivePaper" onClick={() => onUseInLivePaper?.(savedId)} />
            <span className="self-center text-xs text-emerald-300">Saved as #{savedId}</span>
          </>
        ) : null}
      </div>
    </div>
  );
}

function ValidationSaveActions({
  validationResult,
  saveContext,
  disabled,
  onSaved,
  onUseInBacktest,
  onUseInLivePaper,
}: {
  validationResult: StrategyValidationResult;
  saveContext: SaveContext;
  disabled?: boolean;
  onSaved?: (parameterSetId: number, approved: boolean) => void;
  onUseInBacktest?: (parameterSetId: number) => void;
  onUseInLivePaper?: (parameterSetId: number) => void;
}) {
  const [saving, setSaving] = useState(false);
  const [savedId, setSavedId] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [allowResearchSave, setAllowResearchSave] = useState(false);

  const failed = validationResult.validationStatus === 'Failed';
  const zeroTrades = (validationResult.validationMetrics?.tradeCount ?? 0) === 0;
  const canApprove =
    !failed &&
    !zeroTrades &&
    !validationResult.isExploratoryProfile &&
    (validationResult.validationStatus === 'Passed' ||
      (validationResult.validationStatus === 'Warning' && !zeroTrades));

  async function save(approve: boolean, researchOnly = false) {
    setSaving(true);
    setError(null);
    try {
      const response = await strategyResearchApi.saveParameterSet({
        name: buildDefaultParameterSetName(saveContext.strategyCode, saveContext.symbolLabel, saveContext.timeframe),
        strategyCode: saveContext.strategyCode,
        symbolId: saveContext.symbolId,
        timeframe: saveContext.timeframe,
        parameters: validationResult.parameters ?? {},
        trainingRange: saveContext.trainingRange,
        validationRange: saveContext.validationRange,
        trainingMetrics: validationResult.trainingMetrics,
        validationMetrics: validationResult.validationMetrics,
        robustnessScore: validationResult.robustnessScore,
        approve: approve && !researchOnly,
        validationStatus: validationResult.validationStatus,
        validationTradeCount: validationResult.validationMetrics?.tradeCount,
        saveAsFailedResearch: researchOnly,
      });
      setSavedId(response.id);
      onSaved?.(response.id, approve && !researchOnly);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to save parameter set.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="mt-3 space-y-2">
      {failed || zeroTrades ? (
        <p className="text-sm text-amber-300">
          {failed
            ? 'This parameter set failed validation and should not be approved.'
            : 'No trades were produced. Run optimization or adjust strategy settings before saving.'}
        </p>
      ) : null}
      {error ? <p className="text-sm text-red-400">{error}</p> : null}
      <div className="flex flex-wrap gap-2">
        {failed || zeroTrades ? (
          <>
            <label className="flex items-center gap-2 text-xs text-slate-400">
              <input
                type="checkbox"
                checked={allowResearchSave}
                onChange={(e) => setAllowResearchSave(e.target.checked)}
              />
              Save failed run for research
            </label>
            {allowResearchSave ? (
              <ActionButton
                label={saving ? 'Saving…' : 'Save as failed research result'}
                onClick={() => void save(false, true)}
                disabled={disabled || saving}
              />
            ) : null}
          </>
        ) : (
          <ActionButton label={saving ? 'Saving…' : 'Save parameter set'} onClick={() => void save(false)} disabled={disabled || saving} />
        )}
        <ActionButton
          label={saving ? 'Saving…' : 'Save & Approve'}
          onClick={() => void save(true)}
          disabled={disabled || saving || !canApprove}
        />
        {savedId ? (
          <>
            <ActionButton label="Use in Backtest" onClick={() => onUseInBacktest?.(savedId)} />
            <ActionButton label="Use in LivePaper" onClick={() => onUseInLivePaper?.(savedId)} />
            <span className="self-center text-xs text-emerald-300">Saved successfully — parameter set #{savedId}</span>
          </>
        ) : null}
      </div>
    </div>
  );
}

function ActionButton({
  label,
  onClick,
  disabled,
}: {
  label: string;
  onClick: () => void;
  disabled?: boolean;
}) {
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onClick}
      className="rounded-lg border border-slate-700 px-3 py-1.5 text-xs text-slate-200 disabled:opacity-50"
    >
      {label}
    </button>
  );
}

function StatusLists({
  failReasons,
  warnings,
}: {
  failReasons: string[];
  warnings: string[];
}) {
  return (
    <>
      {failReasons.length > 0 ? (
        <ul className="mt-2 list-disc pl-5 text-red-300">
          {failReasons.map((reason) => (
            <li key={reason}>{reason}</li>
          ))}
        </ul>
      ) : null}
      {warnings.length > 0 ? (
        <ul className="mt-2 list-disc pl-5 text-amber-300">
          {warnings.map((warning) => (
            <li key={warning}>{warning}</li>
          ))}
        </ul>
      ) : null}
    </>
  );
}

function MetricsBlock({
  title,
  metrics,
}: {
  title: string;
  metrics?: {
    netPnlPercent: number;
    winRate: number;
    profitFactor: number;
    maxDrawdownPercent: number;
    tradeCount: number;
  };
}) {
  if (!metrics) return null;
  return (
    <div>
      <p className="font-medium text-slate-200">{title}</p>
      <ul className="mt-1 space-y-0.5 text-slate-400">
        <li>Net PnL: {metrics.netPnlPercent.toFixed(2)}%</li>
        <li>Win rate: {metrics.winRate.toFixed(1)}%</li>
        <li>Profit factor: {metrics.profitFactor.toFixed(2)}</li>
        <li>Max DD: {metrics.maxDrawdownPercent.toFixed(2)}%</li>
        <li>Trades: {metrics.tradeCount}</li>
      </ul>
    </div>
  );
}
