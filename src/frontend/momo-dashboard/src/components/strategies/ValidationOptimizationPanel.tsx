import { useState } from 'react';
import { FormPanel } from '@/components/common/FormPanel';
import { SelectField, NumberField } from '@/components/forms/fields';
import {
  strategyResearchApi,
  type ParameterOptimizationResult,
  type StrategyResearchExecutionSettings,
  type StrategyValidationResult,
} from '@/api/strategyResearchApi';
import { OptimizationResultPanel } from '@/components/strategies/OptimizationResultPanel';
import {
  PARAMETER_MODE_OPTIONS,
  VALIDATION_MODE_OPTIONS,
  type ParameterModeValue,
  type ValidationModeValue,
} from '@/constants/validationOptions';
import {
  isVgStrategy,
  VG_RESEARCH_PROFILE_OPTIONS,
  type VgResearchProfileValue,
} from '@/constants/validationProfiles';
import { parseApiClientError } from '@/utils/apiError';

type Props = {
  strategyCode: string;
  exchangeId: number;
  symbolId: number;
  symbolLabel: string;
  timeframe: string;
  fromUtc: string;
  toUtc: string;
  riskProfileId: number;
  initialBalance: number;
  executionSettings?: StrategyResearchExecutionSettings;
  disabled?: boolean;
  eligible: boolean;
  eligibilityReasons?: string[];
  supportsValidation?: boolean;
  supportsOptimization?: boolean;
  validationTitle?: string;
  onUseParameterSet?: (parameterSetId: number) => void;
  onUseInLivePaper?: (parameterSetId: number) => void;
  onRunBacktest?: () => void;
  onExtendDateRange?: () => void;
  onViewDiagnostics?: () => void;
};

export function ValidationOptimizationPanel({
  strategyCode,
  exchangeId,
  symbolId,
  symbolLabel,
  timeframe,
  fromUtc,
  toUtc,
  riskProfileId,
  initialBalance,
  executionSettings,
  disabled,
  eligible,
  eligibilityReasons = [],
  supportsValidation = true,
  supportsOptimization = false,
  validationTitle,
  onUseParameterSet,
  onUseInLivePaper,
  onRunBacktest,
  onExtendDateRange,
  onViewDiagnostics,
}: Props) {
  const [validationMode, setValidationMode] = useState<ValidationModeValue>('InSampleOutOfSample70_30');
  const [optimizationMode, setOptimizationMode] = useState<ParameterModeValue>('ManualOnly');
  const [maxCombinations, setMaxCombinations] = useState<number | ''>(100);
  const [objectivePreset, setObjectivePreset] = useState('Balanced');
  const [vgResearchProfile, setVgResearchProfile] = useState<VgResearchProfileValue>('Balanced');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [validationResult, setValidationResult] = useState<StrategyValidationResult | null>(null);
  const [optimizationResult, setOptimizationResult] = useState<ParameterOptimizationResult | null>(null);

  const canRun = eligible && strategyCode && exchangeId && symbolId && timeframe && fromUtc && toUtc && riskProfileId;
  const showVgProfile = isVgStrategy(strategyCode);

  const sharedPayload = {
    strategyCode,
    exchangeId,
    symbolId,
    timeframe,
    fromUtc,
    toUtc,
    riskProfileId,
    initialBalance,
    ...executionSettings,
    vgResearchProfile: showVgProfile ? vgResearchProfile : undefined,
    autoImportCandles: executionSettings?.autoImportCandles ?? true,
  };

  async function runValidation(profileOverride?: VgResearchProfileValue) {
    if (!canRun) return;
    setLoading(true);
    setError(null);
    try {
      const result = await strategyResearchApi.runValidation({
        ...sharedPayload,
        validationMode,
        vgResearchProfile: showVgProfile ? (profileOverride ?? vgResearchProfile) : undefined,
      });
      setValidationResult(result);
      setOptimizationResult(null);
      if (profileOverride) {
        setVgResearchProfile(profileOverride);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Validation failed.');
    } finally {
      setLoading(false);
    }
  }

  async function runOptimization() {
    if (!canRun || optimizationMode === 'ManualOnly') return;
    setLoading(true);
    setError(null);
    try {
      const result = await strategyResearchApi.runOptimization({
        ...sharedPayload,
        validationMode: 'InSampleOutOfSample70_30',
        optimizationMode,
        objectivePreset,
        maxCombinations: maxCombinations === '' ? 100 : Number(maxCombinations),
      });
      setOptimizationResult(result);
    } catch (e) {
      const parsed = parseApiClientError(e);
      setError(parsed.message);
    } finally {
      setLoading(false);
    }
  }

  return (
    <FormPanel
      title={validationTitle ?? (supportsOptimization ? 'Validation & Optimization' : 'Validate current parameters')}
      description={
        eligible
          ? '70/30 validation tunes on the first 70% of the date range and tests on the last 30%. Good training results do not matter if validation fails.'
          : 'Validation & Optimization requires exactly one exchange, symbol, strategy, and execution timeframe.'
      }
    >
      {!eligible ? (
        <div className="rounded-lg border border-slate-800 bg-slate-950/50 p-3 text-sm text-slate-400">
          <p className="font-medium text-slate-200">Not eligible yet</p>
          <ul className="mt-2 list-disc pl-5">
            {(eligibilityReasons.length > 0
              ? eligibilityReasons
              : ['Select exactly one exchange, one symbol, one strategy, and use a single execution timeframe.']
            ).map((reason) => (
              <li key={reason}>{reason}</li>
            ))}
          </ul>
        </div>
      ) : (
        <>
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            <SelectField
              label="Validation mode"
              value={validationMode}
              onChange={(v) => setValidationMode(v as ValidationModeValue)}
              options={[...VALIDATION_MODE_OPTIONS]}
              disabled={disabled || !supportsValidation}
            />
            <SelectField
              label="Parameter mode"
              value={optimizationMode}
              onChange={(v) => setOptimizationMode(v as ParameterModeValue)}
              options={[...PARAMETER_MODE_OPTIONS]}
              disabled={disabled || !supportsOptimization}
            />
            {showVgProfile ? (
              <SelectField
                label="VG research profile"
                value={vgResearchProfile}
                onChange={(v) => setVgResearchProfile(v as VgResearchProfileValue)}
                options={[...VG_RESEARCH_PROFILE_OPTIONS]}
                disabled={disabled}
              />
            ) : null}
            <SelectField
              label="Objective preset"
              value={objectivePreset}
              onChange={setObjectivePreset}
              options={[
                { value: 'Balanced', label: 'Balanced' },
                { value: 'LowDrawdown', label: 'Low drawdown' },
                { value: 'ProfitFactor', label: 'Profit factor' },
                { value: 'MoreTrades', label: 'More trades' },
              ]}
              disabled={disabled || optimizationMode === 'ManualOnly'}
            />
            <NumberField label="Max combinations" value={maxCombinations} onChange={setMaxCombinations} />
          </div>

          {showVgProfile && vgResearchProfile === 'Exploratory' ? (
            <p className="mt-3 text-sm text-amber-300">Exploratory profile is research only — not final validation.</p>
          ) : null}

          {!supportsValidation ? (
            <p className="mt-3 text-sm text-amber-300">This strategy does not support validation.</p>
          ) : null}
          {!supportsOptimization && optimizationMode !== 'ManualOnly' ? (
            <p className="mt-3 text-sm text-amber-300">This strategy does not support optimization.</p>
          ) : null}

          {error ? <p className="mt-3 text-sm text-red-400">{error}</p> : null}

          <div className="mt-4 flex flex-wrap gap-2">
            <button
              type="button"
              disabled={!canRun || disabled || loading || !supportsValidation || (validationMode === 'None' && optimizationMode === 'ManualOnly')}
              onClick={() => void (optimizationMode === 'ManualOnly' ? runValidation() : runOptimization())}
              className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950 disabled:opacity-50"
            >
              {loading ? 'Running…' : optimizationMode === 'ManualOnly' ? 'Validate current parameters' : 'Run Optimization'}
            </button>
            {validationMode !== 'None' && optimizationMode !== 'ManualOnly' && supportsValidation ? (
              <button
                type="button"
                disabled={!canRun || disabled || loading}
                onClick={() => void runValidation()}
                className="rounded-lg border border-slate-700 px-4 py-2 text-sm text-slate-200 disabled:opacity-50"
              >
                Run Validation Only
              </button>
            ) : null}
          </div>

          <OptimizationResultPanel
            validationResult={validationResult}
            optimizationResult={optimizationResult}
            saveContext={{
              strategyCode,
              symbolId,
              symbolLabel,
              timeframe,
              trainingRange: validationResult?.trainingRange,
              validationRange: validationResult?.validationRange,
              optimizationRunId: optimizationResult?.optimizationRunId,
            }}
            disabled={disabled}
            onSaved={(id, approved) => {
              if (approved) onUseParameterSet?.(id);
            }}
            onUseInBacktest={onUseParameterSet}
            onUseInLivePaper={onUseInLivePaper}
            quickActions={{
              onRunBacktest,
              onRunExploratory: showVgProfile ? () => void runValidation('Exploratory') : undefined,
              onRunOptimization: supportsOptimization ? () => void runOptimization() : undefined,
              onExtendDateRange,
              onViewDiagnostics,
            }}
          />
        </>
      )}
    </FormPanel>
  );
}
