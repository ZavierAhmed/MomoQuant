import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { PageHeader } from '@/components/common/PageHeader';
import { SimulationBanner } from '@/components/common/SimulationBanner';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { FormPanel } from '@/components/common/FormPanel';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { FormActions } from '@/components/forms/FormActions';
import { DateField, NumberField, SelectField, TextField } from '@/components/forms/fields';
import { ExchangeSymbolSelector } from '@/components/strategies/ExchangeSymbolSelector';
import { useReferenceData } from '@/hooks/useReferenceData';
import { useRole } from '@/hooks/useRole';
import { strategyLabApi, type CreateStrategyLabRunRequest, type ExposureSemanticsVersion, type StrategyLabExecutionMode, type StrategyLabStrategy } from '@/api/strategyLabApi';
import { parseApiClientError } from '@/utils/apiError';

const EXECUTION_MODES: { value: StrategyLabExecutionMode; label: string }[] = [
  { value: 'RawStrategy', label: 'Raw Strategy' },
  { value: 'StrategyPlusConfidenceObservation', label: 'Strategy + Confidence Observation' },
  { value: 'StrategyPlusRiskObservation', label: 'Strategy + Risk Observation' },
  { value: 'FullPipelineComparison', label: 'Full Pipeline Comparison' },
];

const SYSTEM_DEFAULT_CONFIDENCE_THRESHOLD = 80;
const EXPLICIT_FUTURES_EXPOSURE_V2 = 4 as const;

function optionalNumber(value: number | ''): number | null {
  return value === '' ? null : value;
}

export function StrategyLabPage() {
  const navigate = useNavigate();
  const { canEdit } = useRole();
  const reference = useReferenceData();
  const [strategies, setStrategies] = useState<StrategyLabStrategy[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const [form, setForm] = useState({
    name: '',
    strategyCode: 'PRICE_STRUCTURE_BREAKOUT_RETEST',
    exchangeId: '' as number | '',
    symbolIds: [] as number[],
    timeframe: '15m',
    fromUtc: '',
    toUtc: '',
    executionMode: 'RawStrategy' as StrategyLabExecutionMode,
    initialBalance: 10000,
    takerFeeRate: 0.0004,
    slippagePercent: 0,
    useSystemDefaultConfidenceThreshold: true,
    customConfidenceThreshold: 70,
    useSystemDefaultRiskSettings: true,
    riskProfileId: '' as number | '',
    riskApprovalThreshold: 50,
    riskPerTradePercent: 0.5,
    preferredLeverage: 10 as number | '',
    maximumLeverage: 10,
    maxNotionalExposurePerSymbolPercent: '' as number | '',
    maxTotalNotionalExposurePercent: '' as number | '',
    maxMarginUsagePerSymbolPercent: 25,
    maxTotalMarginUsagePercent: 50,
    maxConcurrentRiskPercent: 2,
    maxOpenPositions: '' as number | '',
    maxDailyLossPercent: '' as number | '',
    maxDrawdownPercent: '' as number | '',
    minimumRewardRisk: '' as number | '',
    feeEfficiencyHardLimitPercent: '' as number | '',
    exposureSemanticsVersion: EXPLICIT_FUTURES_EXPOSURE_V2 as ExposureSemanticsVersion,
  });

  useEffect(() => {
    strategyLabApi.getStrategies()
      .then((data) => setStrategies(data ?? []))
      .catch((err: unknown) => setError(parseApiClientError(err).message))
      .finally(() => setLoading(false));
  }, []);

  const selectedStrategy = useMemo(
    () => strategies.find((s) => s.code === form.strategyCode),
    [strategies, form.strategyCode],
  );

  const showConfidence = form.executionMode === 'StrategyPlusConfidenceObservation'
    || form.executionMode === 'FullPipelineComparison';
  const showRisk = form.executionMode === 'StrategyPlusRiskObservation'
    || form.executionMode === 'FullPipelineComparison';

  const handleSubmit = async () => {
    const symbolId = form.symbolIds[0];
    if (!form.exchangeId || !symbolId || !form.fromUtc || !form.toUtc) {
      setSubmitError('Exchange, symbol, and date range are required.');
      return;
    }

    setSubmitting(true);
    setSubmitError(null);
    try {
      const request: CreateStrategyLabRunRequest = {
        name: form.name || undefined,
        strategyCode: form.strategyCode,
        exchangeId: Number(form.exchangeId),
        symbolId,
        timeframe: form.timeframe,
        fromUtc: new Date(`${form.fromUtc}T00:00:00Z`).toISOString(),
        toUtc: new Date(`${form.toUtc}T23:59:59Z`).toISOString(),
        executionMode: form.executionMode,
        initialBalance: form.initialBalance,
        takerFeeRate: form.takerFeeRate,
        slippagePercent: form.slippagePercent,
        riskProfileId: showRisk && form.riskProfileId !== '' ? Number(form.riskProfileId) : undefined,
        observationSettings: (showConfidence || showRisk)
          ? {
              confidenceModel: 'StrategySetupQuality/v1',
              useSystemDefaultConfidenceThreshold: form.useSystemDefaultConfidenceThreshold,
              customConfidenceThreshold: form.useSystemDefaultConfidenceThreshold
                ? null
                : form.customConfidenceThreshold,
              effectiveConfidenceThreshold: form.useSystemDefaultConfidenceThreshold
                ? SYSTEM_DEFAULT_CONFIDENCE_THRESHOLD
                : form.customConfidenceThreshold,
              useSystemDefaultRiskSettings: form.useSystemDefaultRiskSettings,
              riskProfileId: showRisk && form.riskProfileId !== '' ? Number(form.riskProfileId) : null,
              riskApprovalThreshold: form.useSystemDefaultRiskSettings ? null : form.riskApprovalThreshold,
              riskPerTradePercent: form.useSystemDefaultRiskSettings ? null : form.riskPerTradePercent,
              preferredLeverage: form.useSystemDefaultRiskSettings ? null : optionalNumber(form.preferredLeverage),
              maximumLeverage: form.useSystemDefaultRiskSettings ? null : form.maximumLeverage,
              maxNotionalExposurePerSymbolPercent: form.useSystemDefaultRiskSettings
                ? null
                : optionalNumber(form.maxNotionalExposurePerSymbolPercent),
              maxTotalNotionalExposurePercent: form.useSystemDefaultRiskSettings
                ? null
                : optionalNumber(form.maxTotalNotionalExposurePercent),
              maxMarginUsagePerSymbolPercent: form.useSystemDefaultRiskSettings
                ? null
                : form.maxMarginUsagePerSymbolPercent,
              maxTotalMarginUsagePercent: form.useSystemDefaultRiskSettings
                ? null
                : form.maxTotalMarginUsagePercent,
              maxConcurrentRiskPercent: form.useSystemDefaultRiskSettings ? null : form.maxConcurrentRiskPercent,
              maxOpenPositions: form.useSystemDefaultRiskSettings ? null : optionalNumber(form.maxOpenPositions),
              maxDailyLossPercent: form.useSystemDefaultRiskSettings ? null : optionalNumber(form.maxDailyLossPercent),
              maxDrawdownPercent: form.useSystemDefaultRiskSettings ? null : optionalNumber(form.maxDrawdownPercent),
              minimumRewardRisk: form.useSystemDefaultRiskSettings ? null : optionalNumber(form.minimumRewardRisk),
              feeEfficiencyHardLimitPercent: form.useSystemDefaultRiskSettings
                ? null
                : optionalNumber(form.feeEfficiencyHardLimitPercent),
              exposureSemanticsVersion: form.useSystemDefaultRiskSettings ? undefined : form.exposureSemanticsVersion,
            }
          : undefined,
      };
      const res = await strategyLabApi.createRun(request);
      navigate(`/strategy-lab/runs/${res.id}`);
    } catch (err: unknown) {
      setSubmitError(parseApiClientError(err).message);
    } finally {
      setSubmitting(false);
    }
  };

  if (loading || reference.loading) return <LoadingState />;
  if (error) {
    return (
      <div>
        <PageHeader
          title="Strategy Laboratory"
          description="Test raw strategy logic before confidence and full risk filters are allowed to affect results."
        />
        <ErrorState
          message={
            error.toLowerCase().includes('not found')
              ? 'Failed to load Strategy Laboratory. Strategy Laboratory API may be unavailable or tables are not migrated.'
              : error
          }
        />
      </div>
    );
  }

  return (
    <div>
      <PageHeader
        title="Strategy Laboratory"
        description="Test raw strategy logic before confidence and full risk filters are allowed to affect results."
      />
      <SimulationBanner message="Strategy Laboratory measures the strategy itself. Confidence and full risk gating are observational by default and do not block raw research trades." />

      <div className="mb-4 rounded-lg border border-sky-800/60 bg-sky-950/30 px-4 py-3 text-sm text-sky-100">
        Strategy Laboratory is separate from Backtesting, Benchmarking, Validation, and SK System Analyzer.
      </div>

      <FormPanel title="Run Setup">
        {submitError ? <ApiErrorAlert message={submitError} /> : null}
        <div className="grid gap-4 md:grid-cols-2">
          <SelectField
            label="Strategy"
            value={form.strategyCode}
            onChange={(value) => setForm((prev) => ({
              ...prev,
              strategyCode: value,
              timeframe: strategies.find((s) => s.code === value)?.preferredTimeframe ?? prev.timeframe,
            }))}
            options={strategies.map((s) => ({ value: s.code, label: `${s.name} v${s.version}` }))}
          />
          <TextField label="Run Name" value={form.name} onChange={(value) => setForm((p) => ({ ...p, name: value }))} />
          <ExchangeSymbolSelector
            selectedExchangeId={form.exchangeId}
            selectedSymbolIds={form.symbolIds}
            onExchangeChange={(exchangeId) => setForm((p) => ({ ...p, exchangeId, symbolIds: [] }))}
            onSymbolsChange={(symbolIds) => setForm((p) => ({ ...p, symbolIds }))}
            multiSelect={false}
          />
          <SelectField
            label="Timeframe"
            value={form.timeframe}
            onChange={(value) => setForm((p) => ({ ...p, timeframe: value }))}
            options={(selectedStrategy?.allowedTimeframes ?? ['15m']).map((tf) => ({ value: tf, label: tf }))}
          />
          <DateField label="From" value={form.fromUtc} onChange={(value) => setForm((p) => ({ ...p, fromUtc: value }))} />
          <DateField label="To" value={form.toUtc} onChange={(value) => setForm((p) => ({ ...p, toUtc: value }))} />
          <SelectField
            label="Execution Mode"
            value={form.executionMode}
            onChange={(value) => setForm((p) => ({ ...p, executionMode: value as StrategyLabExecutionMode }))}
            options={EXECUTION_MODES.map((m) => ({ value: m.value, label: m.label }))}
          />
          <NumberField label="Initial Balance" value={form.initialBalance} onChange={(value) => setForm((p) => ({ ...p, initialBalance: Number(value) || 0 }))} />
          <NumberField label="Taker Fee Rate" value={form.takerFeeRate} onChange={(value) => setForm((p) => ({ ...p, takerFeeRate: Number(value) || 0 }))} step={0.0001} />
          <NumberField label="Slippage %" value={form.slippagePercent} onChange={(value) => setForm((p) => ({ ...p, slippagePercent: Number(value) || 0 }))} step={0.01} />
        </div>

        {showConfidence ? (
          <div className="mt-6 space-y-3 rounded-lg border border-slate-800 p-4">
            <h3 className="text-sm font-semibold uppercase tracking-wide text-slate-300">Confidence Observation Settings</h3>
            <div className="text-sm text-slate-400">Confidence Model: StrategySetupQuality/v1</div>
            <label className="flex items-center gap-2 text-sm text-slate-300">
              <input
                type="checkbox"
                checked={form.useSystemDefaultConfidenceThreshold}
                onChange={(e) => setForm((p) => ({ ...p, useSystemDefaultConfidenceThreshold: e.target.checked }))}
              />
              Use System Default Threshold
            </label>
            {form.useSystemDefaultConfidenceThreshold ? (
              <div className="text-sm text-slate-400">Current system threshold: {SYSTEM_DEFAULT_CONFIDENCE_THRESHOLD} (read-only)</div>
            ) : (
              <NumberField
                label="Custom Confidence Threshold"
                value={form.customConfidenceThreshold}
                onChange={(value) => setForm((p) => ({ ...p, customConfidenceThreshold: Math.min(100, Math.max(0, Number(value) || 0)) }))}
              />
            )}
            <p className="text-xs text-slate-400">
              All raw candidates are still simulated. This threshold only controls the observational Approved/Rejected classification.
            </p>
          </div>
        ) : null}

        {showRisk ? (
          <div className="mt-6 space-y-4 rounded-lg border border-slate-800 p-4">
            <h3 className="text-sm font-semibold uppercase tracking-wide text-slate-300">Risk Observation Settings</h3>
            <div className="grid gap-3 md:grid-cols-2">
              <SelectField
                label="Risk Mode"
                value={form.useSystemDefaultRiskSettings ? 'saved' : 'custom'}
                onChange={(value) => setForm((p) => ({ ...p, useSystemDefaultRiskSettings: value === 'saved' }))}
                options={[
                  { value: 'saved', label: 'Saved Risk Profile' },
                  { value: 'custom', label: 'Custom Risk Configuration' },
                ]}
              />
              <SelectField
                label="Risk Profile"
                value={form.riskProfileId === '' ? '' : String(form.riskProfileId)}
                onChange={(value) => setForm((p) => ({ ...p, riskProfileId: value === '' ? '' : Number(value) }))}
                options={[
                  { value: '', label: 'Select profile' },
                  ...reference.riskProfileOptions.map((o) => ({ value: String(o.value), label: o.label })),
                ]}
              />
            </div>

            <div className="grid gap-3 md:grid-cols-2">
              <div className="rounded-lg border border-slate-800 p-3 text-sm text-slate-300">
                <div className="mb-2 font-medium text-slate-200">Financial Risk Rules</div>
                <div>Risk per trade: {form.useSystemDefaultRiskSettings ? '(from profile)' : `${form.riskPerTradePercent}%`}</div>
                <div>Preferred leverage: {form.useSystemDefaultRiskSettings ? '(from profile)' : form.preferredLeverage === '' ? '(not set)' : `${form.preferredLeverage}x`}</div>
                <div>Max leverage: {form.useSystemDefaultRiskSettings ? '(from profile / default 10x)' : `${form.maximumLeverage}x`}</div>
                <div>Max margin per symbol: {form.useSystemDefaultRiskSettings ? '(from profile)' : `${form.maxMarginUsagePerSymbolPercent}%`}</div>
                <div>Max total margin: {form.useSystemDefaultRiskSettings ? '(from profile)' : `${form.maxTotalMarginUsagePercent}%`}</div>
                <div>Max concurrent risk: {form.useSystemDefaultRiskSettings ? '(from profile)' : `${form.maxConcurrentRiskPercent}%`}</div>
              </div>
              <div className="rounded-lg border border-slate-800 p-3 text-sm text-slate-300">
                <div className="mb-2 font-medium text-slate-200">Eligibility / Policy Rules</div>
                <div>Minimum confidence: (from saved risk profile policy)</div>
                <p className="mt-2 text-xs text-slate-400">
                  Minimum confidence is an eligibility policy loaded from the selected risk profile. It does not affect the candidate financial risk score.
                </p>
              </div>
            </div>

            {!form.useSystemDefaultRiskSettings ? (
              <div className="space-y-5">
                <div>
                  <h4 className="mb-1 text-xs font-semibold uppercase tracking-wide text-slate-400">Risk Approval Threshold</h4>
                  <p className="mb-3 text-xs text-slate-500">
                    Observational score threshold — candidates scoring below this are classified as financially rejected for research comparison.
                  </p>
                  <NumberField
                    label="Risk Approval Threshold"
                    value={form.riskApprovalThreshold}
                    onChange={(v) => setForm((p) => ({ ...p, riskApprovalThreshold: Number(v) || 0 }))}
                    hint="Score from 0–100. Does not block raw simulation."
                  />
                </div>

                <div className="rounded-lg border border-slate-800 p-4">
                  <h4 className="mb-1 text-xs font-semibold uppercase tracking-wide text-slate-300">Financial Risk Settings</h4>
                  <p className="mb-3 text-xs text-slate-500">
                    Futures exposure uses separate notional and margin limits. Example: at 5x leverage, $5,000 notional uses approximately $1,000 initial margin.
                  </p>
                  <div className="grid gap-3 md:grid-cols-2">
                    <NumberField
                      label="Risk Per Trade %"
                      value={form.riskPerTradePercent}
                      onChange={(v) => setForm((p) => ({ ...p, riskPerTradePercent: Number(v) || 0 }))}
                      step={0.1}
                      hint="Account equity at risk if stop is hit."
                    />
                    <NumberField
                      label="Preferred Leverage"
                      value={form.preferredLeverage}
                      onChange={(v) => setForm((p) => ({ ...p, preferredLeverage: v }))}
                      hint="Target leverage for sizing when within limits."
                    />
                    <NumberField
                      label="Max Leverage"
                      value={form.maximumLeverage}
                      onChange={(v) => setForm((p) => ({ ...p, maximumLeverage: Number(v) || 0 }))}
                      hint="Hard cap on leverage for the position."
                    />
                    <NumberField
                      label="Max Notional Exposure Per Symbol %"
                      value={form.maxNotionalExposurePerSymbolPercent}
                      onChange={(v) => setForm((p) => ({ ...p, maxNotionalExposurePerSymbolPercent: v }))}
                      hint="Optional. Position notional as % of equity for this symbol."
                    />
                    <NumberField
                      label="Max Total Notional Exposure %"
                      value={form.maxTotalNotionalExposurePercent}
                      onChange={(v) => setForm((p) => ({ ...p, maxTotalNotionalExposurePercent: v }))}
                      hint="Optional. Combined notional across all symbols."
                    />
                    <NumberField
                      label="Max Margin Usage Per Symbol %"
                      value={form.maxMarginUsagePerSymbolPercent}
                      onChange={(v) => setForm((p) => ({ ...p, maxMarginUsagePerSymbolPercent: Number(v) || 0 }))}
                      hint="Initial margin allocated to this symbol vs equity."
                    />
                    <NumberField
                      label="Max Total Margin Usage %"
                      value={form.maxTotalMarginUsagePercent}
                      onChange={(v) => setForm((p) => ({ ...p, maxTotalMarginUsagePercent: Number(v) || 0 }))}
                      hint="Combined initial margin vs equity."
                    />
                    <NumberField
                      label="Max Concurrent Risk %"
                      value={form.maxConcurrentRiskPercent}
                      onChange={(v) => setForm((p) => ({ ...p, maxConcurrentRiskPercent: Number(v) || 0 }))}
                      hint="Sum of open risk-at-stop across positions."
                    />
                    <NumberField
                      label="Max Open Positions"
                      value={form.maxOpenPositions}
                      onChange={(v) => setForm((p) => ({ ...p, maxOpenPositions: v }))}
                      hint="Optional cap on simultaneous open trades."
                    />
                    <NumberField
                      label="Max Daily Loss %"
                      value={form.maxDailyLossPercent}
                      onChange={(v) => setForm((p) => ({ ...p, maxDailyLossPercent: v }))}
                      hint="Optional daily realized loss budget."
                    />
                    <NumberField
                      label="Max Drawdown %"
                      value={form.maxDrawdownPercent}
                      onChange={(v) => setForm((p) => ({ ...p, maxDrawdownPercent: v }))}
                      hint="Optional peak-to-trough drawdown limit."
                    />
                    <NumberField
                      label="Minimum Reward:Risk"
                      value={form.minimumRewardRisk}
                      onChange={(v) => setForm((p) => ({ ...p, minimumRewardRisk: v }))}
                      step={0.1}
                      hint="Optional minimum R:R for setup eligibility."
                    />
                    <NumberField
                      label="Fee Efficiency Hard Limit %"
                      value={form.feeEfficiencyHardLimitPercent}
                      onChange={(v) => setForm((p) => ({ ...p, feeEfficiencyHardLimitPercent: v }))}
                      hint="Optional max fees as % of target profit."
                    />
                  </div>
                </div>

                <div className="rounded-lg border border-amber-900/50 bg-amber-950/20 p-4">
                  <h4 className="mb-1 text-xs font-semibold uppercase tracking-wide text-amber-200">Eligibility Policy</h4>
                  <p className="text-sm text-amber-100/90">
                    Minimum confidence threshold comes from the saved risk profile selected above. Custom financial settings do not override profile eligibility policy.
                  </p>
                </div>
              </div>
            ) : null}
            <p className="text-xs text-slate-400">
              Financial risk is evaluated for every raw candidate, including those rejected by confidence. Raw outcomes remain preserved.
            </p>
          </div>
        ) : null}

        <FormActions>
          <button
            type="button"
            onClick={() => void handleSubmit()}
            disabled={!canEdit || submitting}
            className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950 disabled:opacity-50"
          >
            {submitting ? 'Starting...' : 'Run Strategy Lab'}
          </button>
        </FormActions>
      </FormPanel>

      <div className="mt-4">
        <Link to="/strategy-lab/runs" className="text-sm text-sky-300 hover:underline">View recent runs</Link>
      </div>
    </div>
  );
}
