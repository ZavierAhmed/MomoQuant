import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { PageHeader } from '@/components/common/PageHeader';
import { SimulationBanner } from '@/components/common/SimulationBanner';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { DataTable } from '@/components/common/DataTable';
import { FormPanel } from '@/components/common/FormPanel';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { ValidationSummary } from '@/components/common/ValidationSummary';
import { FormActions } from '@/components/forms/FormActions';
import { CheckboxField, MultiSelectField, NumberField, SelectField, TextField } from '@/components/forms/fields';
import { DateRangeOnlySelector, dateRangeOnlyToUtc } from '@/components/forms/DateRangeOnlySelector';
import { EXECUTION_MODE_OPTIONS } from '@/constants/tradingOptions';
import { formatDate, formatNumber } from '@/components/common/utils';
import { useAsync } from '@/hooks/useAsync';
import { useReferenceData } from '@/hooks/useReferenceData';
import { useShowDisabledStrategies } from '@/hooks/useSessionPolling';
import { useRole } from '@/hooks/useRole';
import { backtestsApi } from '@/api/backtestsApi';
import { aiApi, type AiSetupAdvisorResponse } from '@/api/aiApi';
import { parseApiClientError, applyFieldErrorsToForm } from '@/utils/apiError';
import { buildUtcRange, validateUtcRangeFields } from '@/utils/formHelpers';
import { requireNumber, requireNumberArray, requireStringArray } from '@/utils/numbers';
import { validateRequired } from '@/utils/formValidation';
import { ValidationOptimizationPanel } from '@/components/strategies/ValidationOptimizationPanel';
import { ExchangeSymbolSelector } from '@/components/strategies/ExchangeSymbolSelector';
import {
  getResolvedTimeframesForForm,
  StrategyAwareTimeframeSelector,
  type TimeframeMode,
} from '@/components/strategies/StrategyAwareTimeframeSelector';
import { ParameterSetMeta, StrategyParameterSetSelector } from '@/components/strategies/StrategyParameterSetSelector';
import { normalizeTimeframe } from '@/constants/timeframes';
import { symbolLabel } from '@/utils/referenceLookups';

export function BacktestingPage() {
  const { canEdit } = useRole();
  const [formErrors, setFormErrors] = useState<Record<string, string>>({});
  const [actionError, setActionError] = useState<string | null>(null);
  const [advisor, setAdvisor] = useState<AiSetupAdvisorResponse | null>(null);
  const [advisorLoading, setAdvisorLoading] = useState(false);
  const [timeframeMode, setTimeframeMode] = useState<TimeframeMode>('StrategyDefault');
  const [customTimeframes, setCustomTimeframes] = useState<string[]>([]);
  const [parameterSetId, setParameterSetId] = useState<number | ''>('');
  const [form, setForm] = useState({
    name: 'Sample Backtest',
    exchangeId: '' as number | '',
    symbolIds: [] as number[],
    fromUtc: '',
    toUtc: '',
    initialBalance: 10000 as number | '',
    riskProfileId: '' as number | '',
    strategyIds: [] as number[],
    executionMode: 'MarketFill',
    makerFeeRate: 0.0002 as number | '',
    takerFeeRate: 0.0004 as number | '',
    orderExpiryCandles: 3 as number | '',
    useAiScoring: false,
    minConfidenceScore: 70 as number | '',
  });

  const reference = useReferenceData(form.exchangeId || null);
  const { showDisabledStrategies, setShowDisabledStrategies } = useShowDisabledStrategies();
  const backtests = useAsync(() => backtestsApi.list({ page: 1, pageSize: 50 }), []);

  const selectedStrategies = useMemo(
    () => reference.strategies.filter((strategy) => form.strategyIds.includes(strategy.id)),
    [reference.strategies, form.strategyIds],
  );

  const resolvedTimeframes = useMemo(
    () => getResolvedTimeframesForForm(selectedStrategies, timeframeMode, customTimeframes),
    [selectedStrategies, timeframeMode, customTimeframes],
  );

  const selectedStrategy = selectedStrategies[0];
  const selectedStrategyCode = selectedStrategy?.code;
  const selectedSymbolId = form.symbolIds[0];
  const selectedTimeframe = resolvedTimeframes[0];

  const researchEligible =
    !!form.exchangeId &&
    form.symbolIds.length === 1 &&
    form.strategyIds.length === 1 &&
    resolvedTimeframes.length === 1 &&
    !!form.riskProfileId &&
    !!form.fromUtc &&
    !!form.toUtc;

  const researchEligibilityReasons = useMemo(() => {
    const reasons: string[] = [];
    if (!form.exchangeId) reasons.push('Select exactly one exchange.');
    if (form.symbolIds.length !== 1) reasons.push('Select exactly one symbol.');
    if (form.strategyIds.length !== 1) reasons.push('Select exactly one strategy.');
    if (resolvedTimeframes.length !== 1) reasons.push('Select a valid execution timeframe.');
    if (!form.fromUtc || !form.toUtc) reasons.push('Select a valid date range.');
    if (!form.riskProfileId) reasons.push('Select a risk profile.');
    if (selectedStrategy && selectedStrategy.supportsValidation === false) {
      reasons.push('This strategy does not support validation.');
    }
    if (selectedStrategy && selectedStrategy.supportsOptimization === false && timeframeMode === 'Custom') {
      reasons.push('Custom timeframe may not be supported for validation on this strategy.');
    }
    return reasons;
  }, [form, resolvedTimeframes, selectedStrategy, timeframeMode]);

  function validateForm() {
    const errors: Record<string, string> = {};
    const nameError = validateRequired(form.name.trim(), 'Backtest name is required.');
    if (nameError) errors.name = nameError;
    if (!form.exchangeId) errors.exchangeId = 'Exchange is required.';
    if (!form.symbolIds.length) errors.symbolIds = 'Select at least one symbol.';
    if (!resolvedTimeframes.length) errors.timeframes = 'Select at least one execution timeframe.';
    if (!form.riskProfileId) errors.riskProfileId = 'Risk profile is required.';
    if (!form.strategyIds.length) errors.strategyIds = 'Select at least one strategy.';
    if (form.initialBalance === '' || Number(form.initialBalance) <= 0) errors.initialBalance = 'Initial balance must be greater than zero.';
    Object.assign(errors, validateUtcRangeFields(form.fromUtc, form.toUtc));
    setFormErrors(errors);
    return Object.keys(errors).length === 0;
  }

  async function handleRun() {
    if (!canEdit || !validateForm()) return;
    setActionError(null);
    const range = buildUtcRange(form.fromUtc, form.toUtc);
    try {
      await backtestsApi.run({
        name: form.name.trim(),
        exchangeId: requireNumber(form.exchangeId, 'Exchange'),
        symbolIds: requireNumberArray(form.symbolIds, 'Symbols'),
        timeframes: requireStringArray(resolvedTimeframes, 'Timeframes').map((tf) => normalizeTimeframe(tf)),
        fromUtc: range.fromUtc,
        toUtc: range.toUtc,
        fromDate: form.fromUtc,
        toDate: form.toUtc,
        autoImportMissingCandles: true,
        initialBalance: requireNumber(form.initialBalance, 'Initial balance'),
        riskProfileId: requireNumber(form.riskProfileId, 'Risk profile'),
        strategyIds: requireNumberArray(form.strategyIds, 'Strategies'),
        executionMode: form.executionMode,
        makerFeeRate: requireNumber(form.makerFeeRate, 'Maker fee'),
        takerFeeRate: requireNumber(form.takerFeeRate, 'Taker fee'),
        orderExpiryCandles: requireNumber(form.orderExpiryCandles, 'Order expiry'),
        useAiScoring: form.useAiScoring,
        minConfidenceScore: requireNumber(form.minConfidenceScore, 'Minimum confidence'),
      });
      backtests.reload();
    } catch (error) {
      const parsed = parseApiClientError(error);
      setActionError(parsed.message);
      setFormErrors((current) => ({ ...current, ...applyFieldErrorsToForm(parsed.fieldErrors, { timeframes: 'timeframes', fromUtc: 'fromUtc', toUtc: 'toUtc' }) }));
    }
  }

  async function askAiAdvisor() {
    if (!form.symbolIds.length || !form.strategyIds.length) {
      setActionError('Select symbols and strategies before asking AI advisor.');
      return;
    }

    setAdvisorLoading(true);
    setActionError(null);
    try {
      const response = await aiApi.setupAdvisor({
        mode: 'Backtest',
        symbolIds: form.symbolIds,
        strategyIds: form.strategyIds,
        fromDate: form.fromUtc ? form.fromUtc.slice(0, 10) : undefined,
        toDate: form.toUtc ? form.toUtc.slice(0, 10) : undefined,
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
    const executionTimeframes = advisor.requiredTimeframes.filter((item) => item !== '4h');
    setForm((current) => ({
      ...current,
      strategyIds: advisor.recommendedStrategies.length > 0 ? advisor.recommendedStrategies : current.strategyIds,
    }));
    if (executionTimeframes.length > 0) {
      setTimeframeMode('Custom');
      setCustomTimeframes(executionTimeframes);
    }
  }

  return (
    <div>
      <PageHeader title="Backtesting" description="Historical simulation runs and performance analysis." />
      <SimulationBanner message="Backtesting uses historical data only. No real orders are placed." />
      <ApiErrorAlert message={actionError} />
      <ValidationSummary errors={formErrors} />

      {canEdit ? (
        <FormPanel title="Run Backtest" description="Configure a historical simulation using stored market data.">
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            <TextField label="Backtest Name" value={form.name} onChange={(v) => setForm((c) => ({ ...c, name: v }))} required error={formErrors.name} />
            <ExchangeSymbolSelector
              selectedExchangeId={form.exchangeId}
              selectedSymbolIds={form.symbolIds}
              onExchangeChange={(exchangeId) => setForm((c) => ({ ...c, exchangeId, symbolIds: [] }))}
              onSymbolsChange={(symbolIds) => setForm((c) => ({ ...c, symbolIds }))}
              required
              exchangeError={formErrors.exchangeId}
              symbolsError={formErrors.symbolIds}
            />
            <MultiSelectField
              label="Strategies"
              values={form.strategyIds}
              onChange={(v) => setForm((c) => ({ ...c, strategyIds: v }))}
              options={reference.buildStrategyOptions(showDisabledStrategies)}
              required
              error={formErrors.strategyIds}
            />
            <StrategyAwareTimeframeSelector
              strategies={selectedStrategies}
              timeframeMode={timeframeMode}
              customTimeframes={customTimeframes}
              onTimeframeModeChange={setTimeframeMode}
              onCustomTimeframesChange={setCustomTimeframes}
              error={formErrors.timeframes}
            />
            <SelectField label="Risk Profile" value={form.riskProfileId} onChange={(v) => setForm((c) => ({ ...c, riskProfileId: v }))} options={reference.riskProfileOptions} required error={formErrors.riskProfileId} />
            <div id="backtest-date-range">
            <DateRangeOnlySelector
              fromDate={form.fromUtc}
              toDate={form.toUtc}
              onChange={({ fromDate, toDate }) => setForm((c) => ({ ...c, fromUtc: fromDate, toUtc: toDate }))}
              required
              errors={{ fromDate: formErrors.fromUtc, toDate: formErrors.toUtc }}
            />
            </div>
            <SelectField label="Execution Mode" value={form.executionMode} onChange={(v) => setForm((c) => ({ ...c, executionMode: v || 'MarketFill' }))} options={EXECUTION_MODE_OPTIONS} />
            <NumberField label="Initial Balance" value={form.initialBalance} onChange={(v) => setForm((c) => ({ ...c, initialBalance: v }))} required error={formErrors.initialBalance} />
            <StrategyParameterSetSelector
              strategyCode={form.strategyIds.length === 1 ? selectedStrategyCode : undefined}
              symbolId={form.symbolIds.length === 1 ? selectedSymbolId : undefined}
              timeframe={resolvedTimeframes.length === 1 ? selectedTimeframe : undefined}
              selectedParameterSetId={parameterSetId}
              onChange={setParameterSetId}
              onRunValidation={() => document.getElementById('validation-optimization-panel')?.scrollIntoView({ behavior: 'smooth' })}
              onRunOptimization={() => document.getElementById('validation-optimization-panel')?.scrollIntoView({ behavior: 'smooth' })}
            />
            <ParameterSetMeta parameterSetId={parameterSetId} />
            <NumberField label="Maker Fee Rate" value={form.makerFeeRate} onChange={(v) => setForm((c) => ({ ...c, makerFeeRate: v }))} step={0.0001} />
            <NumberField label="Taker Fee Rate" value={form.takerFeeRate} onChange={(v) => setForm((c) => ({ ...c, takerFeeRate: v }))} step={0.0001} />
            <NumberField label="Order Expiry (candles)" value={form.orderExpiryCandles} onChange={(v) => setForm((c) => ({ ...c, orderExpiryCandles: v }))} />
            <CheckboxField label="Show disabled strategies" checked={showDisabledStrategies} onChange={setShowDisabledStrategies} />
            <CheckboxField label="Use AI scoring" checked={form.useAiScoring} onChange={(v) => setForm((c) => ({ ...c, useAiScoring: v }))} hint="AI is advisory only." />
            <NumberField label="Minimum Confidence Score" value={form.minConfidenceScore} onChange={(v) => setForm((c) => ({ ...c, minConfidenceScore: v }))} min={0} max={100} />
          </div>
          <FormActions><button type="button" onClick={() => void handleRun()} className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950">Run Backtest</button></FormActions>
          <FormActions>
            <button type="button" onClick={() => void askAiAdvisor()} className="rounded-lg border border-slate-700 px-4 py-2 text-sm text-slate-200">
              {advisorLoading ? 'Asking AI…' : 'Ask AI Setup Advisor'}
            </button>
            {advisor ? (
              <button type="button" onClick={applyAiSuggestions} className="rounded-lg border border-emerald-700 px-4 py-2 text-sm text-emerald-200">
                Apply AI Suggestions
              </button>
            ) : null}
          </FormActions>
          {advisor ? (
            <div className="mt-3 rounded-lg border border-slate-800 p-3 text-xs text-slate-300">
              <p className="font-medium text-slate-100">{advisor.summary}</p>
              <p>Required timeframes: {advisor.requiredTimeframes.join(', ')}</p>
              {advisor.suggestions.map((item) => <p key={item}>- {item}</p>)}
            </div>
          ) : null}
        </FormPanel>
      ) : null}

      <div id="validation-optimization-panel">
      <ValidationOptimizationPanel
        strategyCode={selectedStrategyCode ?? ''}
        exchangeId={Number(form.exchangeId) || 0}
        symbolId={selectedSymbolId ?? 0}
        symbolLabel={symbolLabel(reference.symbols, selectedSymbolId, reference.exchanges)}
        timeframe={selectedTimeframe ?? ''}
        fromUtc={form.fromUtc ? dateRangeOnlyToUtc(form.fromUtc, form.toUtc).fromUtc : ''}
        toUtc={form.toUtc ? dateRangeOnlyToUtc(form.fromUtc, form.toUtc).toUtc : ''}
        riskProfileId={Number(form.riskProfileId) || 0}
        initialBalance={Number(form.initialBalance) || 10000}
        executionSettings={{
          executionMode: form.executionMode,
          makerFeeRate: form.makerFeeRate === '' ? undefined : Number(form.makerFeeRate),
          takerFeeRate: form.takerFeeRate === '' ? undefined : Number(form.takerFeeRate),
          orderExpiryCandles: form.orderExpiryCandles === '' ? undefined : Number(form.orderExpiryCandles),
          useAiScoring: form.useAiScoring,
          minConfidenceScore: form.minConfidenceScore === '' ? undefined : Number(form.minConfidenceScore),
          autoImportCandles: true,
        }}
        disabled={!canEdit}
        eligible={researchEligible}
        eligibilityReasons={researchEligibilityReasons}
        supportsValidation={selectedStrategy?.supportsValidation ?? true}
        supportsOptimization={selectedStrategy?.supportsOptimization ?? false}
        onUseParameterSet={(id) => setParameterSetId(id)}
        onRunBacktest={() => void handleRun()}
        onExtendDateRange={() => {
          const panel = document.getElementById('backtest-date-range');
          panel?.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }}
        onViewDiagnostics={() => {
          const panel = document.getElementById('validation-optimization-panel');
          panel?.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }}
      />
      </div>

      {backtests.loading ? <LoadingState /> : null}
      {backtests.error ? <ErrorState message={backtests.error} onRetry={backtests.reload} /> : null}

      <DataTable
        columns={[
          { key: 'name', header: 'Name', render: (row) => row.name },
          { key: 'status', header: 'Status', render: (row) => row.status },
          { key: 'balance', header: 'Final Balance', render: (row) => formatNumber(row.finalBalance) },
          { key: 'created', header: 'Created', render: (row) => formatDate(row.createdAtUtc) },
          { key: 'view', header: '', render: (row) => <Link to={`/backtesting/${row.id}`} className="text-xs underline">View</Link> },
        ]}
        rows={backtests.data?.items ?? []}
      />
    </div>
  );
}
