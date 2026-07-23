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
import { strategyLabApi, type StrategyLabStrategy } from '@/api/strategyLabApi';
import {
  validationLabApi,
  type CreateValidationExperimentRequest,
  type ValidationExperimentType,
  type ValidationPrimaryQualificationLayer,
  type ValidationQualificationProfile,
} from '@/api/validationLabApi';
import { parseApiClientError } from '@/utils/apiError';

const STEPS = [
  'Strategy',
  'Market Data',
  'Split',
  'Strategy Parameters',
  'Fixed Overlays',
  'Qualification Profile',
  'Review + Create',
] as const;

const EXPERIMENT_TYPES: { value: ValidationExperimentType; label: string }[] = [
  { value: 'ValidateExistingFrozenConfiguration', label: 'Validate Existing Frozen Configuration' },
  { value: 'TrainingSearchHoldoutValidation', label: 'Training Search + Holdout Validation' },
];

const PRIMARY_LAYERS: { value: ValidationPrimaryQualificationLayer; label: string }[] = [
  { value: 'RawStrategy', label: 'Raw Strategy (recommended)' },
  { value: 'ConfidenceQualified', label: 'Confidence Qualified' },
  { value: 'RiskOnly', label: 'Risk Only' },
  { value: 'FullPipeline', label: 'Full Pipeline' },
];

const DEFAULT_QUALIFICATION: ValidationQualificationProfile = {
  profileVersion: 'StandardHoldoutQualification/v1',
  primaryQualificationLayer: 'RawStrategy',
  minimumTrainingClosedTrades: 30,
  minimumValidationClosedTrades: 15,
  minimumTrainingProfitFactor: 1.1,
  minimumValidationProfitFactor: 1.05,
  minimumTrainingNetExpectancyR: 0,
  minimumValidationNetExpectancyR: 0,
  maximumTrainingDrawdownPercent: 25,
  maximumValidationDrawdownPercent: 25,
  minimumOpportunityRetentionPercent: 40,
  maximumAllowedExpectancyDegradation: 0.5,
  maximumSingleTradePnlContributionPercent: 40,
  requirePositiveValidationNetPnl: true,
  requirePositiveValidationNetExpectancy: true,
  requireParameterStability: true,
};

function parseParamsJson(raw: string): Record<string, string> | undefined {
  const trimmed = raw.trim();
  if (!trimmed) return undefined;
  try {
    const parsed = JSON.parse(trimmed) as Record<string, unknown>;
    const result: Record<string, string> = {};
    Object.entries(parsed).forEach(([key, value]) => {
      result[key] = String(value);
    });
    return result;
  } catch {
    return undefined;
  }
}

export function ValidationLabNewPage() {
  const navigate = useNavigate();
  const { canEdit } = useRole();
  const reference = useReferenceData();
  const [step, setStep] = useState(0);
  const [strategies, setStrategies] = useState<StrategyLabStrategy[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const [form, setForm] = useState({
    name: '',
    description: '',
    experimentType: 'ValidateExistingFrozenConfiguration' as ValidationExperimentType,
    strategyCode: 'PRICE_STRUCTURE_BREAKOUT_RETEST',
    strategyVersion: '',
    sourceStrategyLabRunId: '' as number | '',
    exchangeId: '' as number | '',
    symbolIds: [] as number[],
    timeframe: '15m',
    fromUtc: '',
    toUtc: '',
    autoImportMissingCandles: true,
    splitRatio: 0.7,
    requiredWarmupCandles: 100,
    strategyParametersJson: '{\n  \n}',
    parameterSearchNote: true,
    maximumTrials: 50,
    deterministicSeed: 42,
    primaryQualificationLayer: 'RawStrategy' as ValidationPrimaryQualificationLayer,
    useSystemDefaultConfidenceThreshold: false,
    customConfidenceThreshold: 65,
    useSystemDefaultRiskSettings: false,
    riskPerTradePercent: 0.5,
    preferredLeverage: 10,
    maximumLeverage: 10,
    maxMarginUsagePerSymbolPercent: 25,
    maxTotalMarginUsagePercent: 50,
    maxConcurrentRiskPercent: 2,
    maxOpenPositions: 2,
    maxDailyLossPercent: 2,
    maxDrawdownPercent: 5,
    initialBalance: 10000,
    makerFeeRate: 0.0002,
    takerFeeRate: 0.0004,
    slippagePercent: 0,
    qualification: { ...DEFAULT_QUALIFICATION },
  });

  useEffect(() => {
    strategyLabApi
      .getStrategies()
      .then((data) => {
        setStrategies(data ?? []);
        const first = data?.[0];
        if (first) {
          setForm((prev) => ({
            ...prev,
            strategyCode: first.code,
            strategyVersion: first.version,
            timeframe: first.preferredTimeframe ?? prev.timeframe,
          }));
        }
      })
      .catch((err: unknown) => setError(parseApiClientError(err).message))
      .finally(() => setLoading(false));
  }, []);

  const selectedStrategy = useMemo(
    () => strategies.find((s) => s.code === form.strategyCode),
    [strategies, form.strategyCode],
  );

  const isSearch = form.experimentType === 'TrainingSearchHoldoutValidation';

  const goNext = () => {
    setSubmitError(null);
    if (step === 0 && !form.strategyCode) {
      setSubmitError('Strategy is required.');
      return;
    }
    if (step === 1) {
      if (!form.exchangeId || !form.symbolIds[0] || !form.fromUtc || !form.toUtc) {
        setSubmitError('Exchange, symbol, and date range are required.');
        return;
      }
    }
    if (step === 3 && !isSearch) {
      const params = parseParamsJson(form.strategyParametersJson);
      if (form.strategyParametersJson.trim() && form.strategyParametersJson.trim() !== '{\n  \n}' && !params) {
        setSubmitError('Strategy parameters must be valid JSON object.');
        return;
      }
    }
    setStep((s) => Math.min(s + 1, STEPS.length - 1));
  };

  const goBack = () => {
    setSubmitError(null);
    setStep((s) => Math.max(s - 1, 0));
  };

  const handleCreate = async () => {
    const symbolId = form.symbolIds[0];
    if (!form.exchangeId || !symbolId || !form.fromUtc || !form.toUtc) {
      setSubmitError('Exchange, symbol, and date range are required.');
      return;
    }

    const strategyParameters = parseParamsJson(form.strategyParametersJson);
    if (form.strategyParametersJson.trim() && form.strategyParametersJson.trim() !== '{\n  \n}' && !strategyParameters) {
      setSubmitError('Strategy parameters must be valid JSON object.');
      return;
    }

    setSubmitting(true);
    setSubmitError(null);
    try {
      const request: CreateValidationExperimentRequest = {
        name: form.name || undefined,
        description: form.description || undefined,
        experimentType: form.experimentType,
        strategyCode: form.strategyCode,
        strategyVersion: form.strategyVersion || selectedStrategy?.version || undefined,
        sourceStrategyLabRunId:
          form.sourceStrategyLabRunId === '' ? undefined : Number(form.sourceStrategyLabRunId),
        exchangeId: Number(form.exchangeId),
        symbolId,
        timeframe: form.timeframe,
        requestedStartUtc: new Date(`${form.fromUtc}T00:00:00Z`).toISOString(),
        requestedEndUtc: new Date(`${form.toUtc}T23:59:59Z`).toISOString(),
        splitRatio: form.splitRatio,
        requiredWarmupCandles: form.requiredWarmupCandles,
        strategyParameters,
        maximumTrials: form.maximumTrials,
        deterministicSeed: form.deterministicSeed,
        primaryQualificationLayer: form.primaryQualificationLayer,
        qualificationProfile: {
          ...form.qualification,
          primaryQualificationLayer: form.primaryQualificationLayer,
        },
        initialBalance: form.initialBalance,
        makerFeeRate: form.makerFeeRate,
        takerFeeRate: form.takerFeeRate,
        slippagePercent: form.slippagePercent,
        autoImportMissingCandles: form.autoImportMissingCandles,
        observationSettings: {
          confidenceModel: 'StrategySetupQuality/v1',
          useSystemDefaultConfidenceThreshold: form.useSystemDefaultConfidenceThreshold,
          customConfidenceThreshold: form.useSystemDefaultConfidenceThreshold
            ? null
            : form.customConfidenceThreshold,
          effectiveConfidenceThreshold: form.useSystemDefaultConfidenceThreshold
            ? 80
            : form.customConfidenceThreshold,
          useSystemDefaultRiskSettings: form.useSystemDefaultRiskSettings,
          riskPerTradePercent: form.useSystemDefaultRiskSettings ? null : form.riskPerTradePercent,
          preferredLeverage: form.useSystemDefaultRiskSettings ? null : form.preferredLeverage,
          maximumLeverage: form.useSystemDefaultRiskSettings ? null : form.maximumLeverage,
          maxMarginUsagePerSymbolPercent: form.useSystemDefaultRiskSettings
            ? null
            : form.maxMarginUsagePerSymbolPercent,
          maxTotalMarginUsagePercent: form.useSystemDefaultRiskSettings
            ? null
            : form.maxTotalMarginUsagePercent,
          maxConcurrentRiskPercent: form.useSystemDefaultRiskSettings ? null : form.maxConcurrentRiskPercent,
          maxOpenPositions: form.useSystemDefaultRiskSettings ? null : form.maxOpenPositions,
          maxDailyLossPercent: form.useSystemDefaultRiskSettings ? null : form.maxDailyLossPercent,
          maxDrawdownPercent: form.useSystemDefaultRiskSettings ? null : form.maxDrawdownPercent,
          exposureSemanticsVersion: 4,
        },
      };

      const created = await validationLabApi.createExperiment(request);
      navigate(`/validation-lab/experiments/${created.id}`);
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
        <PageHeader title="New Validation Experiment" description="Create a holdout validation experiment." />
        <ErrorState message={error} />
      </div>
    );
  }

  return (
    <div>
      <PageHeader
        title="New Validation Experiment"
        description="Wizard for training-only selection, freeze, and unseen holdout validation."
      />
      <SimulationBanner message="Validation Laboratory never optimizes confidence or risk thresholds. Overlays stay fixed." />

      <div className="mb-4 flex flex-wrap gap-2">
        {STEPS.map((label, index) => (
          <button
            key={label}
            type="button"
            onClick={() => setStep(index)}
            className={`rounded-lg px-3 py-1.5 text-xs ${
              step === index ? 'bg-slate-700 text-white' : 'border border-slate-800 text-slate-400'
            }`}
          >
            {index + 1}. {label}
          </button>
        ))}
      </div>

      <FormPanel title={STEPS[step]}>
        {submitError ? <ApiErrorAlert message={submitError} /> : null}

        {step === 0 ? (
          <div className="grid gap-4 md:grid-cols-2">
            <SelectField
              label="Strategy"
              value={form.strategyCode}
              onChange={(value) =>
                setForm((prev) => ({
                  ...prev,
                  strategyCode: value,
                  strategyVersion: strategies.find((s) => s.code === value)?.version ?? prev.strategyVersion,
                  timeframe: strategies.find((s) => s.code === value)?.preferredTimeframe ?? prev.timeframe,
                }))
              }
              options={strategies.map((s) => ({ value: s.code, label: `${s.name} v${s.version}` }))}
            />
            <TextField
              label="Strategy Version"
              value={form.strategyVersion}
              onChange={(value) => setForm((p) => ({ ...p, strategyVersion: value }))}
            />
            <SelectField
              label="Experiment Type"
              value={form.experimentType}
              onChange={(value) =>
                setForm((p) => ({ ...p, experimentType: value as ValidationExperimentType }))
              }
              options={EXPERIMENT_TYPES.map((t) => ({ value: t.value, label: t.label }))}
            />
            <NumberField
              label="Source Strategy Lab Run Id (optional)"
              value={form.sourceStrategyLabRunId === '' ? '' : form.sourceStrategyLabRunId}
              onChange={(value) =>
                setForm((p) => ({
                  ...p,
                  sourceStrategyLabRunId: value === '' ? '' : Number(value) || '',
                }))
              }
            />
            <TextField
              label="Experiment Name"
              value={form.name}
              onChange={(value) => setForm((p) => ({ ...p, name: value }))}
            />
            <TextField
              label="Description"
              value={form.description}
              onChange={(value) => setForm((p) => ({ ...p, description: value }))}
            />
          </div>
        ) : null}

        {step === 1 ? (
          <div className="grid gap-4 md:grid-cols-2">
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
            <DateField
              label="Start Date (UTC)"
              value={form.fromUtc}
              onChange={(value) => setForm((p) => ({ ...p, fromUtc: value }))}
            />
            <DateField
              label="End Date (UTC)"
              value={form.toUtc}
              onChange={(value) => setForm((p) => ({ ...p, toUtc: value }))}
            />
            <label className="flex items-center gap-2 text-sm text-slate-300 md:col-span-2">
              <input
                type="checkbox"
                checked={form.autoImportMissingCandles}
                onChange={(e) => setForm((p) => ({ ...p, autoImportMissingCandles: e.target.checked }))}
              />
              Auto-import missing candles
            </label>
            <p className="text-sm text-slate-400 md:col-span-2">
              Data-quality preview and candle fingerprint are computed after Prepare Data on the experiment detail page.
            </p>
          </div>
        ) : null}

        {step === 2 ? (
          <div className="space-y-4">
            <div className="grid gap-4 md:grid-cols-2">
              <NumberField
                label="Split Ratio (training)"
                value={form.splitRatio}
                onChange={(value) => setForm((p) => ({ ...p, splitRatio: Number(value) || 0.7 }))}
                step={0.01}
              />
              <NumberField
                label="Required Warmup Candles"
                value={form.requiredWarmupCandles}
                onChange={(value) => setForm((p) => ({ ...p, requiredWarmupCandles: Number(value) || 0 }))}
              />
            </div>
            <div className="rounded-lg border border-sky-800/60 bg-sky-950/30 px-4 py-3 text-sm text-sky-100">
              Default chronological split is 70% training / 30% unseen validation by candle count (not candidates).
              Exact training and validation date ranges and candle counts appear after Prepare Data.
            </div>
            <div className="grid gap-3 md:grid-cols-2">
              <div className="rounded-lg border border-slate-800 px-3 py-2 text-sm">
                <div className="text-xs uppercase tracking-wide text-slate-500">Training</div>
                <div className="text-slate-200">Used for parameter selection only.</div>
              </div>
              <div className="rounded-lg border border-slate-800 px-3 py-2 text-sm">
                <div className="text-xs uppercase tracking-wide text-slate-500">Validation</div>
                <div className="text-slate-200">Hidden from optimization; evaluated only after freeze.</div>
              </div>
            </div>
          </div>
        ) : null}

        {step === 3 ? (
          <div className="space-y-4">
            {!isSearch ? (
              <>
                <p className="text-sm text-slate-400">
                  Provide fixed strategy parameters as JSON. These will be frozen before holdout validation.
                </p>
                <label className="block text-sm text-slate-300">
                  Strategy Parameters (JSON)
                  <textarea
                    className="mt-1 w-full rounded-lg border border-slate-700 bg-slate-950 px-3 py-2 font-mono text-sm text-slate-100"
                    rows={10}
                    value={form.strategyParametersJson}
                    onChange={(e) => setForm((p) => ({ ...p, strategyParametersJson: e.target.value }))}
                  />
                </label>
              </>
            ) : (
              <>
                <div className="rounded-lg border border-amber-800/50 bg-amber-950/20 px-4 py-3 text-sm text-amber-100">
                  Training search uses the strategy&apos;s bounded default search space. Seed and maximum trials
                  control deterministic trial order. Validation metrics never influence ranking.
                </div>
                <div className="grid gap-4 md:grid-cols-2">
                  <NumberField
                    label="Maximum Trials"
                    value={form.maximumTrials}
                    onChange={(value) => setForm((p) => ({ ...p, maximumTrials: Number(value) || 1 }))}
                  />
                  <NumberField
                    label="Deterministic Seed"
                    value={form.deterministicSeed}
                    onChange={(value) => setForm((p) => ({ ...p, deterministicSeed: Number(value) || 0 }))}
                  />
                </div>
                <label className="block text-sm text-slate-300">
                  Optional base parameters (JSON)
                  <textarea
                    className="mt-1 w-full rounded-lg border border-slate-700 bg-slate-950 px-3 py-2 font-mono text-sm text-slate-100"
                    rows={6}
                    value={form.strategyParametersJson}
                    onChange={(e) => setForm((p) => ({ ...p, strategyParametersJson: e.target.value }))}
                  />
                </label>
              </>
            )}
          </div>
        ) : null}

        {step === 4 ? (
          <div className="space-y-4">
            <div className="rounded-lg border border-sky-800/60 bg-sky-950/30 px-4 py-3 text-sm text-sky-100">
              These settings will not be optimized.
            </div>
            <div className="grid gap-4 md:grid-cols-2">
              <NumberField
                label="Confidence Threshold"
                value={form.customConfidenceThreshold}
                onChange={(value) => setForm((p) => ({ ...p, customConfidenceThreshold: Number(value) || 0 }))}
              />
              <NumberField
                label="Risk Per Trade %"
                value={form.riskPerTradePercent}
                onChange={(value) => setForm((p) => ({ ...p, riskPerTradePercent: Number(value) || 0 }))}
                step={0.1}
              />
              <NumberField
                label="Preferred Leverage"
                value={form.preferredLeverage}
                onChange={(value) => setForm((p) => ({ ...p, preferredLeverage: Number(value) || 0 }))}
              />
              <NumberField
                label="Maximum Leverage"
                value={form.maximumLeverage}
                onChange={(value) => setForm((p) => ({ ...p, maximumLeverage: Number(value) || 0 }))}
              />
              <NumberField
                label="Max Margin / Symbol %"
                value={form.maxMarginUsagePerSymbolPercent}
                onChange={(value) => setForm((p) => ({ ...p, maxMarginUsagePerSymbolPercent: Number(value) || 0 }))}
              />
              <NumberField
                label="Max Total Margin %"
                value={form.maxTotalMarginUsagePercent}
                onChange={(value) => setForm((p) => ({ ...p, maxTotalMarginUsagePercent: Number(value) || 0 }))}
              />
              <NumberField
                label="Max Concurrent Risk %"
                value={form.maxConcurrentRiskPercent}
                onChange={(value) => setForm((p) => ({ ...p, maxConcurrentRiskPercent: Number(value) || 0 }))}
              />
              <NumberField
                label="Max Open Positions"
                value={form.maxOpenPositions}
                onChange={(value) => setForm((p) => ({ ...p, maxOpenPositions: Number(value) || 0 }))}
              />
              <NumberField
                label="Max Daily Loss %"
                value={form.maxDailyLossPercent}
                onChange={(value) => setForm((p) => ({ ...p, maxDailyLossPercent: Number(value) || 0 }))}
              />
              <NumberField
                label="Max Drawdown %"
                value={form.maxDrawdownPercent}
                onChange={(value) => setForm((p) => ({ ...p, maxDrawdownPercent: Number(value) || 0 }))}
              />
              <NumberField
                label="Initial Balance"
                value={form.initialBalance}
                onChange={(value) => setForm((p) => ({ ...p, initialBalance: Number(value) || 0 }))}
              />
              <NumberField
                label="Maker Fee Rate"
                value={form.makerFeeRate}
                onChange={(value) => setForm((p) => ({ ...p, makerFeeRate: Number(value) || 0 }))}
                step={0.0001}
              />
              <NumberField
                label="Taker Fee Rate"
                value={form.takerFeeRate}
                onChange={(value) => setForm((p) => ({ ...p, takerFeeRate: Number(value) || 0 }))}
                step={0.0001}
              />
              <NumberField
                label="Slippage %"
                value={form.slippagePercent}
                onChange={(value) => setForm((p) => ({ ...p, slippagePercent: Number(value) || 0 }))}
                step={0.01}
              />
            </div>
          </div>
        ) : null}

        {step === 5 ? (
          <div className="space-y-4">
            <SelectField
              label="Primary Qualification Layer"
              value={form.primaryQualificationLayer}
              onChange={(value) =>
                setForm((p) => ({
                  ...p,
                  primaryQualificationLayer: value as ValidationPrimaryQualificationLayer,
                }))
              }
              options={PRIMARY_LAYERS.map((l) => ({ value: l.value, label: l.label }))}
            />
            {form.primaryQualificationLayer !== 'RawStrategy' ? (
              <div className="rounded-lg border border-amber-800 bg-amber-950/30 px-4 py-3 text-sm text-amber-100">
                Primary qualification layer is not Raw Strategy. Overlay-gated results can look stronger on thin
                samples and must not override a failed raw-strategy verdict without caution.
              </div>
            ) : null}
            <div className="grid gap-4 md:grid-cols-2">
              <NumberField
                label="Min Training Closed Trades"
                value={form.qualification.minimumTrainingClosedTrades ?? 30}
                onChange={(value) =>
                  setForm((p) => ({
                    ...p,
                    qualification: { ...p.qualification, minimumTrainingClosedTrades: Number(value) || 0 },
                  }))
                }
              />
              <NumberField
                label="Min Validation Closed Trades"
                value={form.qualification.minimumValidationClosedTrades ?? 15}
                onChange={(value) =>
                  setForm((p) => ({
                    ...p,
                    qualification: { ...p.qualification, minimumValidationClosedTrades: Number(value) || 0 },
                  }))
                }
              />
              <NumberField
                label="Min Training Profit Factor"
                value={form.qualification.minimumTrainingProfitFactor ?? 1.1}
                onChange={(value) =>
                  setForm((p) => ({
                    ...p,
                    qualification: { ...p.qualification, minimumTrainingProfitFactor: Number(value) || 0 },
                  }))
                }
                step={0.01}
              />
              <NumberField
                label="Min Validation Profit Factor"
                value={form.qualification.minimumValidationProfitFactor ?? 1.05}
                onChange={(value) =>
                  setForm((p) => ({
                    ...p,
                    qualification: { ...p.qualification, minimumValidationProfitFactor: Number(value) || 0 },
                  }))
                }
                step={0.01}
              />
              <NumberField
                label="Max Training Drawdown %"
                value={form.qualification.maximumTrainingDrawdownPercent ?? 25}
                onChange={(value) =>
                  setForm((p) => ({
                    ...p,
                    qualification: { ...p.qualification, maximumTrainingDrawdownPercent: Number(value) || 0 },
                  }))
                }
              />
              <NumberField
                label="Max Validation Drawdown %"
                value={form.qualification.maximumValidationDrawdownPercent ?? 25}
                onChange={(value) =>
                  setForm((p) => ({
                    ...p,
                    qualification: { ...p.qualification, maximumValidationDrawdownPercent: Number(value) || 0 },
                  }))
                }
              />
              <NumberField
                label="Min Opportunity Retention %"
                value={form.qualification.minimumOpportunityRetentionPercent ?? 40}
                onChange={(value) =>
                  setForm((p) => ({
                    ...p,
                    qualification: {
                      ...p.qualification,
                      minimumOpportunityRetentionPercent: Number(value) || 0,
                    },
                  }))
                }
              />
              <NumberField
                label="Max Expectancy Degradation"
                value={form.qualification.maximumAllowedExpectancyDegradation ?? 0.5}
                onChange={(value) =>
                  setForm((p) => ({
                    ...p,
                    qualification: {
                      ...p.qualification,
                      maximumAllowedExpectancyDegradation: Number(value) || 0,
                    },
                  }))
                }
                step={0.01}
              />
            </div>
            <div className="flex flex-col gap-2 text-sm text-slate-300">
              <label className="flex items-center gap-2">
                <input
                  type="checkbox"
                  checked={form.qualification.requirePositiveValidationNetPnl ?? true}
                  onChange={(e) =>
                    setForm((p) => ({
                      ...p,
                      qualification: { ...p.qualification, requirePositiveValidationNetPnl: e.target.checked },
                    }))
                  }
                />
                Require positive validation net PnL
              </label>
              <label className="flex items-center gap-2">
                <input
                  type="checkbox"
                  checked={form.qualification.requirePositiveValidationNetExpectancy ?? true}
                  onChange={(e) =>
                    setForm((p) => ({
                      ...p,
                      qualification: {
                        ...p.qualification,
                        requirePositiveValidationNetExpectancy: e.target.checked,
                      },
                    }))
                  }
                />
                Require positive validation net expectancy
              </label>
              <label className="flex items-center gap-2">
                <input
                  type="checkbox"
                  checked={form.qualification.requireParameterStability ?? true}
                  onChange={(e) =>
                    setForm((p) => ({
                      ...p,
                      qualification: { ...p.qualification, requireParameterStability: e.target.checked },
                    }))
                  }
                />
                Require parameter stability
              </label>
            </div>
          </div>
        ) : null}

        {step === 6 ? (
          <div className="space-y-4 text-sm text-slate-300">
            <div className="rounded-lg border border-slate-800 p-4">
              <div className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">Review</div>
              <dl className="grid gap-2 md:grid-cols-2">
                <div>
                  <dt className="text-slate-500">Strategy</dt>
                  <dd>
                    {form.strategyCode} {form.strategyVersion ? `v${form.strategyVersion}` : ''}
                  </dd>
                </div>
                <div>
                  <dt className="text-slate-500">Experiment Type</dt>
                  <dd>{form.experimentType}</dd>
                </div>
                <div>
                  <dt className="text-slate-500">Market</dt>
                  <dd>
                    Exchange #{form.exchangeId || '—'} / Symbol #{form.symbolIds[0] || '—'} / {form.timeframe}
                  </dd>
                </div>
                <div>
                  <dt className="text-slate-500">Requested Range</dt>
                  <dd>
                    {form.fromUtc || '—'} → {form.toUtc || '—'}
                  </dd>
                </div>
                <div>
                  <dt className="text-slate-500">Split</dt>
                  <dd>
                    {(form.splitRatio * 100).toFixed(0)}% / {((1 - form.splitRatio) * 100).toFixed(0)}% chronological
                  </dd>
                </div>
                <div>
                  <dt className="text-slate-500">Estimated Trials</dt>
                  <dd>{isSearch ? form.maximumTrials : 1}</dd>
                </div>
                <div>
                  <dt className="text-slate-500">Primary Layer</dt>
                  <dd>{form.primaryQualificationLayer}</dd>
                </div>
                <div>
                  <dt className="text-slate-500">Auto-import</dt>
                  <dd>{form.autoImportMissingCandles ? 'Yes' : 'No'}</dd>
                </div>
              </dl>
            </div>
            <p className="text-slate-400">
              Training and unseen validation ranges, candle fingerprint, and exact counts are computed after
              Prepare Data. Validation performance stays hidden until freeze + reveal.
            </p>
          </div>
        ) : null}

        <FormActions>
          {step > 0 ? (
            <button
              type="button"
              onClick={goBack}
              className="rounded-lg border border-slate-700 px-4 py-2 text-sm text-slate-200"
            >
              Back
            </button>
          ) : null}
          {step < STEPS.length - 1 ? (
            <button
              type="button"
              onClick={goNext}
              className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950"
            >
              Next
            </button>
          ) : (
            <button
              type="button"
              disabled={!canEdit || submitting}
              onClick={handleCreate}
              className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950 disabled:opacity-50"
            >
              {submitting ? 'Creating…' : 'Create Experiment'}
            </button>
          )}
          <Link to="/validation-lab" className="text-sm text-sky-300 hover:underline">
            Cancel
          </Link>
        </FormActions>
      </FormPanel>
    </div>
  );
}
