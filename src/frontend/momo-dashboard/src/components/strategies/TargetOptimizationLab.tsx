import { useState } from 'react';
import { FormPanel } from '@/components/common/FormPanel';
import { CheckboxField, NumberField, SelectField } from '@/components/forms/fields';
import { DateRangeOnlySelector, dateRangeOnlyToUtc } from '@/components/forms/DateRangeOnlySelector';
import { ExchangeSymbolSelector } from '@/components/strategies/ExchangeSymbolSelector';
import { StrategyAwareTimeframeSelector, type TimeframeMode } from '@/components/strategies/StrategyAwareTimeframeSelector';
import { ValidationOptimizationPanel } from '@/components/strategies/ValidationOptimizationPanel';
import { TargetOptimizationResultPanel } from '@/components/strategies/TargetOptimizationResultPanel';
import { strategyResearchApi, type TargetOptimizationRun } from '@/api/strategyResearchApi';
import type { StrategyCatalogDetail } from '@/api/domainTypes';
import { useReferenceData } from '@/hooks/useReferenceData';
import { useExchangeSymbols } from '@/hooks/useExchangeSymbols';
import {
  DEFAULT_TARGET_RULES,
  PARAMETER_SEARCH_MODE_OPTIONS,
  TARGET_RULES_PRESETS,
  TARGET_RULES_PRESET_OPTIONS,
  type ParameterSearchMode,
  type TargetOptimizationRules,
  type TargetRulesPreset,
} from '@/constants/targetOptimizationRules';
import { EXECUTION_MODE_OPTIONS } from '@/constants/tradingOptions';
import { normalizeTimeframe } from '@/constants/timeframes';

type Props = {
  strategy: StrategyCatalogDetail;
};

export function TargetOptimizationLab({ strategy }: Props) {
  const [exchangeId, setExchangeId] = useState<number | ''>('');
  const [symbolIds, setSymbolIds] = useState<number[]>([]);
  const [timeframeMode, setTimeframeMode] = useState<TimeframeMode>('StrategyDefault');
  const [customTimeframes, setCustomTimeframes] = useState<string[]>([]);
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');
  const [initialBalance, setInitialBalance] = useState<number | ''>(10000);
  const [riskProfileId, setRiskProfileId] = useState<number | ''>('');
  const [makerFeeRate, setMakerFeeRate] = useState<number | ''>(0.0002);
  const [takerFeeRate, setTakerFeeRate] = useState<number | ''>(0.0004);
  const [slippagePercent, setSlippagePercent] = useState<number | ''>(0);
  const [executionMode, setExecutionMode] = useState('MarketFill');
  const [parameterSearchMode, setParameterSearchMode] = useState<ParameterSearchMode>('GridSearch');
  const [rulesPreset, setRulesPreset] = useState<TargetRulesPreset>('Balanced');
  const [targetRules, setTargetRules] = useState<TargetOptimizationRules>(DEFAULT_TARGET_RULES);
  const [maxCombinations, setMaxCombinations] = useState<number | ''>(100);
  const [maxAttempts, setMaxAttempts] = useState<number | ''>(100);
  const [maxRuntimeMinutes, setMaxRuntimeMinutes] = useState<number | ''>(30);
  const [autoImportMissingCandles, setAutoImportMissingCandles] = useState(true);
  const [saveBestIfPassed, setSaveBestIfPassed] = useState(false);
  const [autoApproveIfPassed, setAutoApproveIfPassed] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [runResult, setRunResult] = useState<TargetOptimizationRun | null>(null);

  const reference = useReferenceData(exchangeId || null);
  const exchangeSymbols = useExchangeSymbols(exchangeId || null);
  const symbolId = symbolIds[0];
  const selectedSymbol = exchangeSymbols.symbols.find((item) => item.id === symbolId);
  const resolvedTimeframe = timeframeMode === 'Custom'
    ? customTimeframes[0] ?? strategy.preferredTimeframe ?? '15m'
    : strategy.preferredTimeframe ?? strategy.allowedTimeframes?.[0] ?? '15m';
  const { fromUtc, toUtc } = dateRangeOnlyToUtc(fromDate, toDate);

  const canOptimize = strategy.supportsOptimization && (strategy.parameterDefinitions?.length ?? 0) > 0;
  const canRun = canOptimize && exchangeId && symbolId && resolvedTimeframe && fromDate && toDate && riskProfileId;

  const validationEligible = canRun && strategy.supportsValidation;

  function applyPreset(preset: TargetRulesPreset) {
    setRulesPreset(preset);
    if (preset !== 'Custom') {
      setTargetRules(TARGET_RULES_PRESETS[preset]);
    }
  }

  function updateRule<K extends keyof TargetOptimizationRules>(key: K, value: TargetOptimizationRules[K]) {
    setRulesPreset('Custom');
    setTargetRules((current) => ({ ...current, [key]: value }));
  }

  async function runTargetOptimization() {
    if (!canRun || !exchangeId || !symbolId || !riskProfileId) return;
    setLoading(true);
    setError(null);
    try {
      const result = await strategyResearchApi.runTargetOptimization({
        strategyCode: strategy.code,
        exchangeId,
        symbolId,
        timeframe: normalizeTimeframe(resolvedTimeframe),
        fromDate,
        toDate,
        parameterSearchMode,
        targetRules: targetRules as unknown as Record<string, unknown>,
        maxCombinations: maxCombinations === '' ? 100 : Number(maxCombinations),
        maxAttempts: maxAttempts === '' ? 100 : Number(maxAttempts),
        maxRuntimeMinutes: maxRuntimeMinutes === '' ? 30 : Number(maxRuntimeMinutes),
        initialBalance: initialBalance === '' ? 10000 : Number(initialBalance),
        riskProfileId,
        makerFeeRate: makerFeeRate === '' ? 0.0002 : Number(makerFeeRate),
        takerFeeRate: takerFeeRate === '' ? 0.0004 : Number(takerFeeRate),
        slippagePercent: slippagePercent === '' ? 0 : Number(slippagePercent),
        executionMode,
        autoImportMissingCandles,
        saveBestIfPassed,
        autoApproveIfPassed: autoApproveIfPassed || targetRules.autoApproveIfPassed,
      });
      setRunResult(result);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Target optimization failed.');
    } finally {
      setLoading(false);
    }
  }

  const symbolName = selectedSymbol?.symbol ?? (symbolId ? `Symbol ${symbolId}` : 'Symbol');

  if (!canOptimize) {
    return (
      <FormPanel
        title="Optimization Lab"
        description="This strategy does not support automatic parameter optimization yet."
      >
        <p className="text-sm text-slate-400">
          Parameter search requires optimizable parameter definitions. Use Validate current parameters on supported strategies instead.
        </p>
      </FormPanel>
    );
  }

  return (
    <div className="space-y-6">
      <FormPanel
        title="Optimization Lab"
        description="Search for parameters on 70% training data, then validate the best set on 30% unseen data."
      >
        <p className="mb-4 text-sm text-slate-400">
          The system will tune parameters on the first 70% of the selected date range. Then it will test the best parameters on the last 30%, which was not used for tuning.
        </p>
        <p className="mb-4 rounded-lg border border-amber-900/40 bg-amber-950/20 p-3 text-sm text-amber-100">
          Good training results alone are not enough. A parameter set is only approved if it also performs well on unseen validation data.
        </p>

        <div className="grid gap-4 lg:grid-cols-2">
          <ExchangeSymbolSelector
            selectedExchangeId={exchangeId}
            selectedSymbolIds={symbolIds}
            onExchangeChange={setExchangeId}
            onSymbolsChange={setSymbolIds}
            multiSelect={false}
            required
          />
          <StrategyAwareTimeframeSelector
            strategies={[strategy]}
            timeframeMode={timeframeMode}
            customTimeframes={customTimeframes}
            onTimeframeModeChange={setTimeframeMode}
            onCustomTimeframesChange={setCustomTimeframes}
          />
          <DateRangeOnlySelector
            fromDate={fromDate}
            toDate={toDate}
            onChange={({ fromDate: nextFrom, toDate: nextTo }) => {
              setFromDate(nextFrom);
              setToDate(nextTo);
            }}
            required
            maxRangeDays={365}
          />
          <SelectField
            label="Risk profile"
            value={riskProfileId}
            onChange={setRiskProfileId}
            options={reference.riskProfileOptions}
            required
          />
          <NumberField label="Initial balance" value={initialBalance} onChange={setInitialBalance} required />
          <SelectField label="Execution mode" value={executionMode} onChange={setExecutionMode} options={EXECUTION_MODE_OPTIONS} />
          <NumberField label="Maker fee rate" value={makerFeeRate} onChange={setMakerFeeRate} step={0.0001} />
          <NumberField label="Taker fee rate" value={takerFeeRate} onChange={setTakerFeeRate} step={0.0001} />
          <NumberField label="Slippage %" value={slippagePercent} onChange={setSlippagePercent} step={0.01} />
          <SelectField
            label="Parameter search mode"
            value={parameterSearchMode}
            onChange={(value) => setParameterSearchMode(value as ParameterSearchMode)}
            options={PARAMETER_SEARCH_MODE_OPTIONS.map((option) => ({ value: option.value, label: option.label }))}
          />
          <NumberField label="Max combinations" value={maxCombinations} onChange={setMaxCombinations} />
          <NumberField label="Max attempts" value={maxAttempts} onChange={setMaxAttempts} />
          <NumberField label="Max runtime (minutes)" value={maxRuntimeMinutes} onChange={setMaxRuntimeMinutes} />
        </div>

        <div className="mt-4 rounded-lg border border-slate-800 p-4">
          <SelectField
            label="Target rules preset"
            value={rulesPreset}
            onChange={(value) => applyPreset(value as TargetRulesPreset)}
            options={TARGET_RULES_PRESET_OPTIONS.map((option) => ({ value: option.value, label: option.label }))}
          />
          <div className="mt-3 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            <NumberField label="Min training PnL %" value={targetRules.minTrainingNetPnlPercent} onChange={(v) => updateRule('minTrainingNetPnlPercent', Number(v))} />
            <NumberField label="Min validation PnL %" value={targetRules.minValidationNetPnlPercent} onChange={(v) => updateRule('minValidationNetPnlPercent', Number(v))} />
            <NumberField label="Min training PF" value={targetRules.minTrainingProfitFactor} onChange={(v) => updateRule('minTrainingProfitFactor', Number(v))} />
            <NumberField label="Min validation PF" value={targetRules.minValidationProfitFactor} onChange={(v) => updateRule('minValidationProfitFactor', Number(v))} />
            <NumberField label="Max training DD %" value={targetRules.maxTrainingDrawdownPercent} onChange={(v) => updateRule('maxTrainingDrawdownPercent', Number(v))} />
            <NumberField label="Max validation DD %" value={targetRules.maxValidationDrawdownPercent} onChange={(v) => updateRule('maxValidationDrawdownPercent', Number(v))} />
            <NumberField label="Min training trades" value={targetRules.minTrainingTrades} onChange={(v) => updateRule('minTrainingTrades', Number(v))} />
            <NumberField label="Min validation trades" value={targetRules.minValidationTrades} onChange={(v) => updateRule('minValidationTrades', Number(v))} />
            <NumberField label="Min robustness score" value={targetRules.minRobustnessScore} onChange={(v) => updateRule('minRobustnessScore', Number(v))} />
          </div>
        </div>

        <div className="mt-4 flex flex-wrap gap-4">
          <CheckboxField label="Auto-import missing candles" checked={autoImportMissingCandles} onChange={setAutoImportMissingCandles} />
          <CheckboxField label="Save best if passed" checked={saveBestIfPassed} onChange={setSaveBestIfPassed} />
          <CheckboxField label="Auto-approve if passed" checked={autoApproveIfPassed} onChange={setAutoApproveIfPassed} />
        </div>

        <div className="mt-4 flex flex-wrap gap-2">
          <button
            type="button"
            disabled={!canRun || loading}
            onClick={runTargetOptimization}
            className="rounded-lg bg-emerald-600 px-4 py-2 text-sm font-medium text-white disabled:opacity-50"
          >
            {loading ? 'Running target optimization…' : 'Find better parameters'}
          </button>
        </div>
        {error ? <p className="mt-3 text-sm text-rose-300">{error}</p> : null}
      </FormPanel>

      {runResult ? (
        <TargetOptimizationResultPanel run={runResult} />
      ) : null}

      {validationEligible ? (
        <ValidationOptimizationPanel
          strategyCode={strategy.code}
          exchangeId={exchangeId as number}
          symbolId={symbolId!}
          symbolLabel={symbolName}
          timeframe={resolvedTimeframe}
          fromUtc={fromUtc}
          toUtc={toUtc}
          riskProfileId={riskProfileId as number}
          initialBalance={initialBalance === '' ? 10000 : Number(initialBalance)}
          executionSettings={{
            executionMode,
            makerFeeRate: makerFeeRate === '' ? 0.0002 : Number(makerFeeRate),
            takerFeeRate: takerFeeRate === '' ? 0.0004 : Number(takerFeeRate),
            slippagePercent: slippagePercent === '' ? 0 : Number(slippagePercent),
            autoImportCandles: autoImportMissingCandles,
          }}
          eligible={validationEligible}
          supportsValidation={strategy.supportsValidation}
          supportsOptimization={false}
        />
      ) : null}
    </div>
  );
}
