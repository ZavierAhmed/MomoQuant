import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { PageHeader } from '@/components/common/PageHeader';
import { SimulationBanner } from '@/components/common/SimulationBanner';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { DataTable } from '@/components/common/DataTable';
import { FormPanel } from '@/components/common/FormPanel';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { ValidationSummary } from '@/components/common/ValidationSummary';
import { StatusPill } from '@/components/common/StatusPill';
import { FormActions } from '@/components/forms/FormActions';
import { CheckboxField, DateField, MultiSelectField, NumberField, SelectField, TextField } from '@/components/forms/fields';
import { EXECUTION_MODE_OPTIONS } from '@/constants/tradingOptions';
import { formatDate, formatNumber } from '@/components/common/utils';
import { useAsync } from '@/hooks/useAsync';
import { useReferenceData } from '@/hooks/useReferenceData';
import { useRole } from '@/hooks/useRole';
import { useShowDisabledStrategies } from '@/hooks/useSessionPolling';
import { strategyBenchmarksApi } from '@/api/strategyBenchmarksApi';
import type { Strategy } from '@/api/domainTypes';
import { BbStrategySettingsPanel, DEFAULT_BB_STRATEGY_SETTINGS, type BbStrategySettingsForm } from '@/components/strategies/BbStrategySettingsPanel';
import { aiApi, type AiSetupAdvisorResponse } from '@/api/aiApi';
import { parseApiClientError } from '@/utils/apiError';
import { requireNumber, requireNumberArray, requireStringArray } from '@/utils/numbers';
import { ExchangeSymbolSelector } from '@/components/strategies/ExchangeSymbolSelector';
import {
  StrategyAwareTimeframeSelector,
  type TimeframeMode,
} from '@/components/strategies/StrategyAwareTimeframeSelector';
import {
  getCommonCustomTimeframes,
  getStrategyPreferredTimeframe,
  getStrategyRequiredDataTimeframes,
} from '@/utils/strategyTimeframes';
import { useExchangeSymbols } from '@/hooks/useExchangeSymbols';
import { timeframeLabel } from '@/constants/timeframes';

const DEFAULT_SYMBOLS = ['BNBUSDT', 'BTCUSDT', 'ETHUSDT'];

export function StrategyBenchmarksPage() {
  const { canEdit } = useRole();
  const [formErrors, setFormErrors] = useState<Record<string, string>>({});
  const [actionError, setActionError] = useState<string | null>(null);
  const [advisor, setAdvisor] = useState<AiSetupAdvisorResponse | null>(null);
  const [advisorLoading, setAdvisorLoading] = useState(false);
  const [preflightLoading, setPreflightLoading] = useState(false);
  const [preflight, setPreflight] = useState<Awaited<ReturnType<typeof strategyBenchmarksApi.preflight>> | null>(null);
  const { showDisabledStrategies, setShowDisabledStrategies } = useShowDisabledStrategies();
  const [timeframeMode, setTimeframeMode] = useState<TimeframeMode>('StrategyDefault');
  const [customTimeframes, setCustomTimeframes] = useState<string[]>([]);
  const [form, setForm] = useState({
    name: 'June 2026 Strategy Benchmark - BNB BTC ETH',
    exchangeId: '' as number | '',
    symbolIds: [] as number[],
    symbols: [] as string[],
    timeframes: ['5m'] as string[],
    executionTimeframeMode: 'AutoSelectByStrategy' as 'AutoSelectByStrategy' | 'AdvancedManualOverride',
    strategyExecutionScope: 'PreferredOnly' as 'PreferredOnly' | 'AllSupported' | 'ManualOverride',
    manualExecutionTimeframes: [] as string[],
    strategyIds: [] as number[],
    benchmarkFromDate: '2026-06-01',
    benchmarkToDate: '2026-06-30',
    warmupFromDate: '2026-05-25',
    initialBalance: 10000 as number | '',
    riskProfileId: '' as number | '',
    executionMode: 'MarketFill',
    evaluationMode: 'RawStrategyResearch' as 'RawStrategyResearch' | 'RiskOnlyResearch' | 'ConfidenceOnlyResearch' | 'FullValidation',
    enableShadowTradeAnalysis: true,
    sameCandleExitPolicy: 'ConservativeStopFirst' as 'ConservativeStopFirst' | 'TargetFirst' | 'OpenHighLowCloseHeuristic',
    useAiScoring: false,
    includeDisabledStrategies: false,
    allowLowCoverage: false,
  });
  const [bbSettings, setBbSettings] = useState<BbStrategySettingsForm>(DEFAULT_BB_STRATEGY_SETTINGS);

  const reference = useReferenceData(form.exchangeId || null);
  const exchangeSymbols = useExchangeSymbols(form.exchangeId || null);
  const benchmarks = useAsync(() => strategyBenchmarksApi.list({ page: 1, pageSize: 50 }), []);

  const selectedStrategies = useMemo(
    () => (reference.strategies ?? []).filter((strategy) => form.strategyIds.includes(strategy.id)),
    [reference.strategies, form.strategyIds],
  );

  const resolvedSymbols = useMemo(() => {
    const byId = new Map(exchangeSymbols.symbols.map((symbol) => [symbol.id, symbol.symbol]));
    return form.symbolIds.map((id) => byId.get(id)).filter((symbol): symbol is string => !!symbol);
  }, [exchangeSymbols.symbols, form.symbolIds]);

  const executionPlan = useMemo(() => {
    if (!resolvedSymbols.length || !selectedStrategies.length) return [];
    return resolvedSymbols.flatMap((symbol) =>
      selectedStrategies.map((strategy) => {
        const executionTf =
          timeframeMode === 'Custom' && customTimeframes[0]
            ? customTimeframes[0]
            : getStrategyPreferredTimeframe(strategy);
        return {
          strategy: strategy.name,
          symbol,
          executionTimeframe: executionTf,
          requiredData: getStrategyRequiredDataTimeframes(strategy, executionTf).join(', '),
          parameterSource: 'Defaults',
        };
      }),
    );
  }, [resolvedSymbols, selectedStrategies, timeframeMode, customTimeframes]);

  const strategyOptions = reference.buildStrategyOptions(showDisabledStrategies || form.includeDisabledStrategies);
  const isBbStrategy = (strategy?: Strategy) =>
    strategy?.name.includes('BB Liquidity Sweep') ?? false;

  const includesBbStrategy = useMemo(
    () => form.strategyIds.some((id) => isBbStrategy(reference.strategies?.find((item) => item.id === id))),
    [form.strategyIds, reference.strategies],
  );

  useEffect(() => {
    if (!exchangeSymbols.symbols.length || form.symbolIds.length) return;
    const defaults = exchangeSymbols.symbols
      .filter((symbol) => DEFAULT_SYMBOLS.includes(symbol.symbol))
      .map((symbol) => symbol.id);
    if (defaults.length) {
      setForm((current) => ({ ...current, symbolIds: defaults }));
    }
  }, [exchangeSymbols.symbols, form.symbolIds.length]);

  useEffect(() => {
    const enabledIds = (reference.strategies ?? []).filter((strategy) => strategy.isEnabled).map((strategy) => strategy.id);
    setForm((current) => {
      if (current.strategyIds.length) return current;
      return { ...current, strategyIds: enabledIds };
    });
  }, [reference.strategies]);

  function validateForm() {
    const errors: Record<string, string> = {};
    if (!form.name.trim()) errors.name = 'Name is required.';
    if (!form.exchangeId) errors.exchangeId = 'Exchange is required.';
    if (!form.symbolIds.length) errors.symbols = 'Select at least one symbol.';
    if (timeframeMode === 'Custom' && !customTimeframes.length) {
      errors.manualExecutionTimeframes = 'Select a compatible custom timeframe.';
    }
    if (timeframeMode === 'Custom' && selectedStrategies.length > 1 && getCommonCustomTimeframes(selectedStrategies).length === 0) {
      errors.manualExecutionTimeframes = 'Selected strategies do not share a common custom timeframe.';
    }
    if (!form.strategyIds.length) errors.strategyIds = 'Select at least one strategy.';
    if (!form.riskProfileId) errors.riskProfileId = 'Risk profile is required.';
    if (!form.benchmarkFromDate) errors.benchmarkFromDate = 'Benchmark from date is required.';
    if (!form.benchmarkToDate) errors.benchmarkToDate = 'Benchmark to date is required.';
    if (!form.warmupFromDate) errors.warmupFromDate = 'Warmup from date is required.';
    if (form.initialBalance === '' || Number(form.initialBalance) <= 0) {
      errors.initialBalance = 'Initial balance must be greater than zero.';
    }
    setFormErrors(errors);
    return Object.keys(errors).length === 0;
  }

  async function handleCreate() {
    if (!canEdit || !validateForm()) return;
    if (preflight?.blockingIssues?.length) {
      setActionError('Benchmark preflight has blocking issues. Resolve them before starting.');
      return;
    }
    setActionError(null);
    const exchange = reference.exchanges.find((item) => item.id === Number(form.exchangeId));
    if (!exchange) {
      setActionError('Selected exchange was not found.');
      return;
    }

    try {
      await strategyBenchmarksApi.create({
        name: form.name.trim(),
        exchangeCode: exchange.code,
        symbols: requireStringArray(resolvedSymbols, 'Symbols'),
        timeframes: requireStringArray(
          timeframeMode === 'Custom' ? customTimeframes : ['5m'],
          'Timeframes',
        ),
        executionTimeframeMode: timeframeMode === 'Custom' ? 'AdvancedManualOverride' : 'AutoSelectByStrategy',
        strategyExecutionScope: timeframeMode === 'Custom' ? 'ManualOverride' : 'PreferredOnly',
        manualExecutionTimeframes: timeframeMode === 'Custom' ? customTimeframes : undefined,
        strategyIds: requireNumberArray(form.strategyIds, 'Strategies'),
        benchmarkFromDate: form.benchmarkFromDate,
        benchmarkToDate: form.benchmarkToDate,
        warmupFromDate: form.warmupFromDate,
        initialBalance: requireNumber(form.initialBalance, 'Initial balance'),
        riskProfileId: requireNumber(form.riskProfileId, 'Risk profile'),
        executionMode: form.executionMode,
        evaluationMode: form.evaluationMode,
        enableShadowTradeAnalysis: form.enableShadowTradeAnalysis,
        sameCandleExitPolicy: form.sameCandleExitPolicy,
        useAiScoring: form.useAiScoring,
        includeDisabledStrategies: form.includeDisabledStrategies,
        importMissingData: true,
        recalculateIndicators: true,
        runEachStrategyIndividually: true,
        allowLowCoverage: form.allowLowCoverage,
      });
      benchmarks.reload();
      setPreflight(null);
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  async function runPreflight() {
    if (!validateForm()) return;
    const exchange = reference.exchanges.find((item) => item.id === Number(form.exchangeId));
    if (!exchange) {
      setActionError('Selected exchange was not found.');
      return;
    }

    setPreflightLoading(true);
    setActionError(null);
    try {
      const response = await strategyBenchmarksApi.preflight({
        exchangeCode: exchange.code,
        symbols: requireStringArray(resolvedSymbols, 'Symbols'),
        strategyIds: requireNumberArray(form.strategyIds, 'Strategies'),
        benchmarkFromDate: form.benchmarkFromDate,
        benchmarkToDate: form.benchmarkToDate,
        warmupFromDate: form.warmupFromDate,
        executionTimeframeMode: timeframeMode === 'Custom' ? 'AdvancedManualOverride' : 'AutoSelectByStrategy',
        strategyExecutionScope: timeframeMode === 'Custom' ? 'ManualOverride' : 'PreferredOnly',
        manualExecutionTimeframes: timeframeMode === 'Custom' ? customTimeframes : undefined,
      });
      setPreflight(response);
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    } finally {
      setPreflightLoading(false);
    }
  }

  async function askAiAdvisor() {
    if (!resolvedSymbols.length || !form.strategyIds.length) {
      setActionError('Select symbols and strategies before asking AI advisor.');
      return;
    }

    setAdvisorLoading(true);
    setActionError(null);
    try {
      const symbolIds = form.symbolIds;
      const response = await aiApi.setupAdvisor({
        mode: 'Benchmark',
        symbolIds,
        strategyIds: form.strategyIds,
        fromDate: form.benchmarkFromDate,
        toDate: form.benchmarkToDate,
        riskProfileId: form.riskProfileId === '' ? undefined : Number(form.riskProfileId),
        executionMode: form.executionMode,
        useAiScoring: form.useAiScoring,
      });
      setAdvisor(response);
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    } finally {
      setAdvisorLoading(false);
    }
  }

  function applyAiSuggestions() {
    if (!advisor) return;
    setForm((current) => ({
      ...current,
      strategyIds: advisor.recommendedStrategies.length > 0 ? advisor.recommendedStrategies : current.strategyIds,
      strategyExecutionScope:
        advisor.recommendedExecutionScope === 'AllSupported'
          ? 'AllSupported'
          : advisor.recommendedExecutionScope === 'ManualOverride'
            ? 'ManualOverride'
            : 'PreferredOnly',
      executionTimeframeMode:
        advisor.recommendedExecutionScope === 'ManualOverride'
          ? 'AdvancedManualOverride'
          : 'AutoSelectByStrategy',
      manualExecutionTimeframes:
        advisor.recommendedExecutionScope === 'ManualOverride'
          ? advisor.requiredTimeframes.filter((value) => value !== '4h')
          : current.manualExecutionTimeframes,
    }));
  }

  const estimatedRuns = preflight?.estimatedTotalRuns ?? (resolvedSymbols.length * Math.max(1, form.strategyIds.length));
  const controlledStrategyIds = useMemo(
    () =>
      (reference.strategies ?? [])
        .filter((strategy) =>
          strategy.name.toUpperCase().includes('EMA PULLBACK') ||
          strategy.name.toUpperCase().includes('4H RANGE RE-ENTRY'))
        .map((strategy) => strategy.id),
    [reference.strategies],
  );

  return (
    <div>
      <PageHeader title="Strategy Benchmarks" description="Compare selected strategies over a historical window using public market data." />
      <SimulationBanner message="Benchmarks use historical public data and simulated backtests only. No real orders are placed." />
      <ApiErrorAlert message={actionError} />
      <ValidationSummary errors={formErrors} />

      {canEdit ? (
        <FormPanel title="Create Benchmark Run" description="Select symbols, timeframes, and strategies. Total runs = symbols × timeframes × strategies.">
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            <TextField label="Name" value={form.name} onChange={(v) => setForm((c) => ({ ...c, name: v }))} required error={formErrors.name} />
            <ExchangeSymbolSelector
              selectedExchangeId={form.exchangeId}
              selectedSymbolIds={form.symbolIds}
              onExchangeChange={(exchangeId) => setForm((c) => ({ ...c, exchangeId, symbolIds: [] }))}
              onSymbolsChange={(symbolIds) => setForm((c) => ({ ...c, symbolIds }))}
              required
              exchangeError={formErrors.exchangeId}
              symbolsError={formErrors.symbols}
            />
            <SelectField label="Risk Profile" value={form.riskProfileId} onChange={(v) => setForm((c) => ({ ...c, riskProfileId: v }))} options={reference.riskProfileOptions} required error={formErrors.riskProfileId} />
            <StrategyAwareTimeframeSelector
              strategies={selectedStrategies}
              timeframeMode={timeframeMode}
              customTimeframes={customTimeframes}
              onTimeframeModeChange={setTimeframeMode}
              onCustomTimeframesChange={setCustomTimeframes}
              error={formErrors.manualExecutionTimeframes}
            />
            <MultiSelectField
              label="Strategies"
              values={form.strategyIds}
              onChange={(v) => setForm((c) => ({ ...c, strategyIds: v }))}
              options={strategyOptions}
              required
              error={formErrors.strategyIds}
            />
            {includesBbStrategy ? (
              <div className="md:col-span-2">
                <BbStrategySettingsPanel
                  value={bbSettings}
                  onChange={setBbSettings}
                  showRsiSettings={form.strategyIds.some((id) => reference.strategies?.find((item) => item.id === id)?.name.includes('RSI Primed') ?? false)}
                />
                <p className="mt-2 text-xs text-slate-500">
                  Persist BB parameter changes on the Strategies page. Benchmark runs use stored strategy parameters; profile defaults are seeded as BalancedResearch.
                </p>
              </div>
            ) : null}
            <SelectField label="Execution Mode" value={form.executionMode} onChange={(v) => setForm((c) => ({ ...c, executionMode: v || 'MarketFill' }))} options={EXECUTION_MODE_OPTIONS} />
            <SelectField
              label="Evaluation Mode"
              value={form.evaluationMode}
              onChange={(v) =>
                setForm((c) => ({
                  ...c,
                  evaluationMode: (v as 'RawStrategyResearch' | 'RiskOnlyResearch' | 'ConfidenceOnlyResearch' | 'FullValidation') || 'RawStrategyResearch',
                }))
              }
              options={[
                { label: 'Raw Strategy Research', value: 'RawStrategyResearch' },
                { label: 'Risk Only Research', value: 'RiskOnlyResearch' },
                { label: 'Confidence Only Research', value: 'ConfidenceOnlyResearch' },
                { label: 'Full Validation', value: 'FullValidation' },
              ]}
            />
            <SelectField
              label="Same Candle Exit Policy"
              value={form.sameCandleExitPolicy}
              onChange={(v) =>
                setForm((c) => ({
                  ...c,
                  sameCandleExitPolicy: (v as 'ConservativeStopFirst' | 'TargetFirst' | 'OpenHighLowCloseHeuristic') || 'ConservativeStopFirst',
                }))
              }
              options={[
                { label: 'Conservative Stop First', value: 'ConservativeStopFirst' },
                { label: 'Target First', value: 'TargetFirst' },
                { label: 'OHLC Heuristic', value: 'OpenHighLowCloseHeuristic' },
              ]}
            />
            <DateField label="Warmup From (UTC)" value={form.warmupFromDate} onChange={(v) => setForm((c) => ({ ...c, warmupFromDate: v }))} required error={formErrors.warmupFromDate} />
            <DateField label="Benchmark From (UTC)" value={form.benchmarkFromDate} onChange={(v) => setForm((c) => ({ ...c, benchmarkFromDate: v }))} required error={formErrors.benchmarkFromDate} />
            <DateField label="Benchmark To (UTC)" value={form.benchmarkToDate} onChange={(v) => setForm((c) => ({ ...c, benchmarkToDate: v }))} required error={formErrors.benchmarkToDate} />
            <NumberField label="Initial Balance" value={form.initialBalance} onChange={(v) => setForm((c) => ({ ...c, initialBalance: v }))} required error={formErrors.initialBalance} />
            <CheckboxField label="Use AI scoring" checked={form.useAiScoring} onChange={(v) => setForm((c) => ({ ...c, useAiScoring: v }))} />
            <CheckboxField label="Enable shadow trade analysis" checked={form.enableShadowTradeAnalysis} onChange={(v) => setForm((c) => ({ ...c, enableShadowTradeAnalysis: v }))} />
            <CheckboxField label="Show disabled strategies" checked={showDisabledStrategies} onChange={setShowDisabledStrategies} />
            <CheckboxField label="Include disabled strategies" checked={form.includeDisabledStrategies} onChange={(v) => setForm((c) => ({ ...c, includeDisabledStrategies: v }))} />
            <CheckboxField label="Allow low coverage" checked={form.allowLowCoverage} onChange={(v) => setForm((c) => ({ ...c, allowLowCoverage: v }))} />
          </div>
          <div className="mt-3 flex flex-wrap gap-2 text-xs">
            <button
              type="button"
              className="rounded border border-slate-700 px-2 py-1 text-slate-300"
              onClick={() =>
                setForm((c) => ({
                  ...c,
                  symbolIds: exchangeSymbols.symbols
                    .filter((symbol) => DEFAULT_SYMBOLS.includes(symbol.symbol))
                    .map((symbol) => symbol.id),
                }))
              }
            >
              Select top benchmark symbols
            </button>
            <button
              type="button"
              className="rounded border border-slate-700 px-2 py-1 text-slate-300"
              onClick={() => setForm((c) => ({ ...c, symbolIds: exchangeSymbols.symbols.map((symbol) => symbol.id) }))}
            >
              Select all active symbols
            </button>
            <button type="button" className="rounded border border-slate-700 px-2 py-1 text-slate-300" onClick={() => setForm((c) => ({ ...c, symbolIds: [] }))}>
              Clear symbols
            </button>
            <button
              type="button"
              className="rounded border border-slate-700 px-2 py-1 text-slate-300"
              onClick={() =>
                setForm((c) => ({
                  ...c,
                  strategyIds: (reference.strategies ?? []).filter((s) => s.isEnabled).map((s) => s.id),
                }))
              }
            >
              Select all enabled strategies
            </button>
            <button type="button" className="rounded border border-slate-700 px-2 py-1 text-slate-300" onClick={() => setForm((c) => ({ ...c, strategyIds: [] }))}>
              Clear strategies
            </button>
            <button
              type="button"
              className="rounded border border-cyan-700 px-2 py-1 text-cyan-200"
              onClick={() =>
                setForm((c) => ({
                  ...c,
                  evaluationMode: 'RawStrategyResearch',
                  useAiScoring: false,
                  enableShadowTradeAnalysis: true,
                  riskProfileId:
                    reference.riskProfiles.find((item) => item.name === 'Benchmark Research Risk')?.id ??
                    c.riskProfileId,
                  executionMode: 'MarketFill',
                }))
              }
            >
              Apply research preset
            </button>
            <button
              type="button"
              className="rounded border border-indigo-700 px-2 py-1 text-indigo-200"
              onClick={() =>
                setForm((c) => ({
                  ...c,
                  evaluationMode: 'FullValidation',
                  useAiScoring: true,
                  enableShadowTradeAnalysis: true,
                  riskProfileId:
                    reference.riskProfiles.find((item) => item.name === 'Paper Validation Risk')?.id ??
                    c.riskProfileId,
                  executionMode: c.executionMode || 'MarketFill',
                }))
              }
            >
              Apply validation preset
            </button>
            <button
              type="button"
              className="rounded border border-emerald-700 px-2 py-1 text-emerald-200"
              onClick={() =>
                setForm((c) => ({
                  ...c,
                  name: 'Debug Benchmark - BNBUSDT 3m 2 Strategies 10 Days',
                  symbolIds: exchangeSymbols.symbols.filter((s) => s.symbol === 'BNBUSDT').map((s) => s.id),
                  timeframes: ['5m'],
                  executionTimeframeMode: 'AutoSelectByStrategy',
                  strategyExecutionScope: 'PreferredOnly',
                  manualExecutionTimeframes: [],
                  strategyIds: controlledStrategyIds,
                  warmupFromDate: '2026-05-25',
                  benchmarkFromDate: '2026-06-01',
                  benchmarkToDate: '2026-06-10',
                  useAiScoring: false,
                  evaluationMode: 'RawStrategyResearch',
                  enableShadowTradeAnalysis: true,
                  executionMode: 'MarketFill',
                  initialBalance: 10000,
                }))
              }
            >
              Use controlled debug benchmark (2 runs)
            </button>
          </div>
          <div className="mt-2 rounded border border-slate-800 bg-slate-950/40 p-2 text-xs text-slate-400">
            {form.evaluationMode === 'RawStrategyResearch'
              ? 'Runs strategy signals with minimal gating so you can discover whether the strategy has potential.'
              : form.evaluationMode === 'RiskOnlyResearch'
                ? 'Applies risk engine but does not hard-block by confidence.'
                : form.evaluationMode === 'ConfidenceOnlyResearch'
                  ? 'Applies confidence threshold but skips full risk constraints except trade sanity checks.'
                  : 'Applies strategy, confidence, risk, execution, fees, and slippage.'}
          </div>
          <p className="mt-3 text-sm text-slate-400">Estimated total runs: {estimatedRuns}</p>
          {executionPlan.length > 0 ? (
            <div className="mt-4 overflow-x-auto rounded-lg border border-slate-800">
              <table className="min-w-full text-left text-xs text-slate-300">
                <thead className="bg-slate-950/70 text-slate-400">
                  <tr>
                    <th className="px-3 py-2">Strategy</th>
                    <th className="px-3 py-2">Symbol</th>
                    <th className="px-3 py-2">Execution TF</th>
                    <th className="px-3 py-2">Required Data</th>
                    <th className="px-3 py-2">Parameter Source</th>
                  </tr>
                </thead>
                <tbody>
                  {executionPlan.map((row) => (
                    <tr key={`${row.strategy}-${row.symbol}`} className="border-t border-slate-800">
                      <td className="px-3 py-2">{row.strategy}</td>
                      <td className="px-3 py-2">{row.symbol}</td>
                      <td className="px-3 py-2">{timeframeLabel(row.executionTimeframe)}</td>
                      <td className="px-3 py-2">{row.requiredData}</td>
                      <td className="px-3 py-2">{row.parameterSource}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : null}
          <div className="mt-3 flex flex-wrap gap-2">
            <button type="button" onClick={() => void askAiAdvisor()} className="rounded-lg border border-slate-600 px-3 py-1.5 text-xs text-slate-200">
              {advisorLoading ? 'Asking AI…' : 'Ask AI Setup Advisor'}
            </button>
            <button type="button" onClick={() => void runPreflight()} className="rounded-lg border border-slate-600 px-3 py-1.5 text-xs text-slate-200">
              {preflightLoading ? 'Running Preflight…' : 'Run Preflight'}
            </button>
            {advisor ? (
              <button type="button" onClick={applyAiSuggestions} className="rounded-lg border border-emerald-700 px-3 py-1.5 text-xs text-emerald-200">
                Apply AI Suggestions
              </button>
            ) : null}
          </div>
          {advisor ? (
            <div className="mt-3 rounded-lg border border-slate-800 p-3 text-sm text-slate-300">
              <p className="font-medium text-slate-100">{advisor.summary}</p>
              <p className="mt-1">Required timeframes: {advisor.requiredTimeframes.join(', ') || '—'}</p>
              <p>Estimated runs: {advisor.estimatedRunCount}</p>
              {advisor.suggestions.map((item) => (
                <p key={item} className="text-xs text-slate-400">- {item}</p>
              ))}
              {advisor.blockingIssues.map((item) => (
                <p key={item} className="text-xs text-rose-300">{item}</p>
              ))}
            </div>
          ) : null}
          {preflight ? (
            <div className="mt-3 rounded-lg border border-slate-800 p-3 text-sm text-slate-300">
              {(() => {
                const anchorData = preflight.requiredImportTimeframes.filter((item) => item.isAnchorData);
                const executionData = preflight.requiredImportTimeframes.filter((item) => !item.isAnchorData);
                return (
                  <>
              <p className="font-medium text-slate-100">Benchmark Preflight</p>
              <p className="mt-1">Estimated total runs: {preflight.estimatedTotalRuns}</p>
              <p>
                Required execution/import timeframes:{' '}
                {Array.from(new Set(executionData.map((item) => item.timeframe))).join(', ') || '—'}
              </p>
              <p>
                Required anchor data:{' '}
                {anchorData.length > 0
                  ? Array.from(new Set(anchorData.map((item) => `${item.timeframe} (anchor data)`))).join(', ')
                  : '—'}
              </p>
              <p>Resolved execution runs:</p>
              {preflight.resolvedExecutionRuns.map((run) => (
                <p key={run.strategyId} className="text-xs text-slate-400">
                  - {run.strategyCode}: execution [{run.executionTimeframes.join(', ')}], data [{run.requiredDataTimeframes.join(', ')}]
                </p>
              ))}
              {anchorData.map((item) => (
                <p key={`${item.symbol}-${item.timeframe}-${item.reason}`} className="text-xs text-sky-300">
                  - {item.symbol} {item.timeframe} (anchor data): {item.reason}
                </p>
              ))}
              {preflight.warnings.map((item) => (
                <p key={item} className="text-xs text-amber-200">{item}</p>
              ))}
              {preflight.blockingIssues.map((item) => (
                <p key={item} className="text-xs text-rose-300">{item}</p>
              ))}
                  </>
                );
              })()}
            </div>
          ) : null}
          <FormActions>
            <button type="button" onClick={() => void handleCreate()} className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950">
              Start Benchmark
            </button>
          </FormActions>
        </FormPanel>
      ) : null}

      <section>
        <h2 className="mb-3 text-sm font-medium text-slate-300">Benchmark Runs</h2>
        {benchmarks.loading ? <LoadingState /> : null}
        {benchmarks.error ? <ErrorState message={benchmarks.error} onRetry={benchmarks.reload} /> : null}
        <DataTable
          columns={[
            { key: 'name', header: 'Name', render: (row) => row.name },
            { key: 'status', header: 'Status', render: (row) => <StatusPill status={row.status} /> },
            {
              key: 'progress',
              header: 'Progress',
              render: (row) => `${formatNumber(row.percentComplete)}% (${row.completedRuns}/${row.totalRuns})`,
            },
            { key: 'stage', header: 'Stage', render: (row) => row.currentStage ?? '—' },
            { key: 'created', header: 'Created', render: (row) => formatDate(row.createdAtUtc) },
            {
              key: 'view',
              header: '',
              render: (row) => (
                <Link to={`/strategy-benchmarks/${row.id}`} className="text-xs underline">
                  View
                </Link>
              ),
            },
          ]}
          rows={benchmarks.data?.items ?? []}
        />
      </section>
    </div>
  );
}
