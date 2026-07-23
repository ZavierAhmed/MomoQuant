import { useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { PageHeader } from '@/components/common/PageHeader';
import { SimulationBanner } from '@/components/common/SimulationBanner';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { EmptyState } from '@/components/common/EmptyState';
import { DataTable } from '@/components/common/DataTable';
import { FormPanel } from '@/components/common/FormPanel';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { ValidationSummary } from '@/components/common/ValidationSummary';
import { StatusPill } from '@/components/common/StatusPill';
import { FormActions } from '@/components/forms/FormActions';
import { CheckboxField, MultiSelectField, NumberField, SelectField, TextField } from '@/components/forms/fields';
import { DateRangeOnlySelector, dateRangeOnlyToUtc } from '@/components/forms/DateRangeOnlySelector';
import { EXECUTION_MODE_OPTIONS, REPLAY_SPEED_OPTIONS, TIMEFRAME_OPTIONS } from '@/constants/tradingOptions';
import { formatDate } from '@/components/common/utils';
import { useAsync } from '@/hooks/useAsync';
import { useReferenceData } from '@/hooks/useReferenceData';
import { useShowDisabledStrategies } from '@/hooks/useSessionPolling';
import { useRole } from '@/hooks/useRole';
import { replayApi } from '@/api/replayApi';
import { aiApi, type AiSetupAdvisorResponse } from '@/api/aiApi';
import { AiScoringFields } from '@/components/trading/AiScoringFields';
import { ExchangeSymbolSelector } from '@/components/strategies/ExchangeSymbolSelector';
import {
  getResolvedTimeframesForForm,
  StrategyAwareTimeframeSelector,
  type TimeframeMode,
} from '@/components/strategies/StrategyAwareTimeframeSelector';
import { exchangeLabel, symbolLabel } from '@/utils/referenceLookups';
import { parseApiClientError } from '@/utils/apiError';
import { validateUtcRangeFields } from '@/utils/formHelpers';
import { requireNumber, requireNumberArray } from '@/utils/numbers';
import { validateRequired } from '@/utils/formValidation';
import { normalizeTimeframe } from '@/constants/timeframes';

type ReplayMode = 'StrategyReplay' | 'CandleOnlyReplay';

export function ReplayPage() {
  const navigate = useNavigate();
  const { canEdit } = useRole();
  const [formErrors, setFormErrors] = useState<Record<string, string>>({});
  const [actionError, setActionError] = useState<string | null>(null);
  const [advisor, setAdvisor] = useState<AiSetupAdvisorResponse | null>(null);
  const [advisorLoading, setAdvisorLoading] = useState(false);
  const [replayMode, setReplayMode] = useState<ReplayMode>('StrategyReplay');
  const [timeframeMode, setTimeframeMode] = useState<TimeframeMode>('StrategyDefault');
  const [customTimeframes, setCustomTimeframes] = useState<string[]>([]);
  const [candleOnlyTimeframe, setCandleOnlyTimeframe] = useState('15m');
  const [form, setForm] = useState({
    name: 'Replay Session',
    exchangeId: '' as number | '',
    symbolIds: [] as number[],
    fromUtc: '',
    toUtc: '',
    initialBalance: 10000 as number | '',
    riskProfileId: '' as number | '',
    strategyIds: [] as number[],
    executionMode: 'MarketFill',
    minConfidenceScore: 80 as number | '',
    speed: 'ManualStep',
  });

  const reference = useReferenceData(form.exchangeId || null);
  const { showDisabledStrategies, setShowDisabledStrategies } = useShowDisabledStrategies();
  const sessions = useAsync(() => replayApi.list({ page: 1, pageSize: 50 }), []);
  const [aiScoring, setAiScoring] = useState({ useAiScoring: false, strictAiRequired: false });

  const selectedStrategies = useMemo(
    () => reference.strategies.filter((strategy) => form.strategyIds.includes(strategy.id)),
    [reference.strategies, form.strategyIds],
  );

  const resolvedTimeframes = useMemo(() => {
    if (replayMode === 'CandleOnlyReplay') {
      return [candleOnlyTimeframe];
    }
    return getResolvedTimeframesForForm(selectedStrategies, timeframeMode, customTimeframes);
  }, [replayMode, candleOnlyTimeframe, selectedStrategies, timeframeMode, customTimeframes]);

  const selectedSymbolId = form.symbolIds[0];
  const selectedTimeframe = resolvedTimeframes[0];

  function validateForm() {
    const errors: Record<string, string> = {};
    if (validateRequired(form.name.trim(), 'Session name is required.')) errors.name = 'Session name is required.';
    if (!form.exchangeId) errors.exchangeId = 'Exchange is required.';
    if (form.symbolIds.length !== 1) errors.symbolIds = 'Select exactly one symbol.';
    if (!form.riskProfileId) errors.riskProfileId = 'Risk profile is required.';
    if (replayMode === 'StrategyReplay' && !form.strategyIds.length) errors.strategyIds = 'Select at least one strategy.';
    if (replayMode === 'StrategyReplay' && resolvedTimeframes.length !== 1) errors.timeframes = 'Select one execution timeframe.';
    if (form.initialBalance === '' || Number(form.initialBalance) <= 0) errors.initialBalance = 'Initial balance must be greater than zero.';
    Object.assign(errors, validateUtcRangeFields(form.fromUtc, form.toUtc));
    setFormErrors(errors);
    return Object.keys(errors).length === 0;
  }

  async function createSession() {
    if (!canEdit || !validateForm()) return;
    setActionError(null);
    const range = dateRangeOnlyToUtc(form.fromUtc, form.toUtc);
    try {
      const created = await replayApi.create({
        name: form.name.trim(),
        exchangeId: requireNumber(form.exchangeId, 'Exchange'),
        symbolId: requireNumber(selectedSymbolId, 'Symbol'),
        timeframe: normalizeTimeframe(selectedTimeframe),
        fromUtc: range.fromUtc,
        toUtc: range.toUtc,
        fromDate: form.fromUtc,
        toDate: form.toUtc,
        autoImportMissingCandles: true,
        initialBalance: requireNumber(form.initialBalance, 'Initial balance'),
        riskProfileId: requireNumber(form.riskProfileId, 'Risk profile'),
        strategyIds: replayMode === 'CandleOnlyReplay' ? [] : requireNumberArray(form.strategyIds, 'Strategies'),
        executionMode: form.executionMode,
        useAiScoring: aiScoring.useAiScoring,
        strictAiRequired: aiScoring.strictAiRequired,
        minConfidenceScore: requireNumber(form.minConfidenceScore, 'Minimum confidence'),
        speed: form.speed,
      });
      sessions.reload();
      navigate(`/replay/${created.id}`);
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  async function askAiAdvisor() {
    if (!selectedSymbolId || (replayMode === 'StrategyReplay' && !form.strategyIds.length)) {
      setActionError('Select symbol and strategies before asking AI advisor.');
      return;
    }

    setAdvisorLoading(true);
    setActionError(null);
    try {
      const response = await aiApi.setupAdvisor({
        mode: 'Replay',
        symbolIds: [Number(selectedSymbolId)],
        strategyIds: form.strategyIds,
        fromDate: form.fromUtc || undefined,
        toDate: form.toUtc || undefined,
        riskProfileId: form.riskProfileId === '' ? undefined : Number(form.riskProfileId),
        executionMode: form.executionMode,
        useAiScoring: aiScoring.useAiScoring,
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
    }));
    const executionTimeframes = advisor.requiredTimeframes.filter((item) => item !== '4h');
    if (executionTimeframes.length > 0) {
      setTimeframeMode('Custom');
      setCustomTimeframes(executionTimeframes);
    }
  }

  return (
    <div>
      <PageHeader title="Replay" description="Historical debugging sessions with step controls." />
      <SimulationBanner message="Replay is for historical debugging only. No real orders are placed." />
      <ApiErrorAlert message={actionError} />
      <ValidationSummary errors={formErrors} />

      {canEdit ? (
        <FormPanel title="Create Replay Session" description="Step through historical candles for debugging.">
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            <TextField label="Session Name" value={form.name} onChange={(v) => setForm((c) => ({ ...c, name: v }))} required error={formErrors.name} />
            <SelectField
              label="Replay Mode"
              value={replayMode}
              onChange={(v) => setReplayMode((v as ReplayMode) || 'StrategyReplay')}
              options={[
                { value: 'StrategyReplay', label: 'Strategy Replay' },
                { value: 'CandleOnlyReplay', label: 'Candle Only Replay' },
              ]}
            />
            <ExchangeSymbolSelector
              selectedExchangeId={form.exchangeId}
              selectedSymbolIds={form.symbolIds}
              onExchangeChange={(exchangeId) => setForm((c) => ({ ...c, exchangeId, symbolIds: [] }))}
              onSymbolsChange={(symbolIds) => setForm((c) => ({ ...c, symbolIds }))}
              multiSelect={false}
              required
              exchangeError={formErrors.exchangeId}
              symbolsError={formErrors.symbolIds}
            />
            {replayMode === 'StrategyReplay' ? (
              <>
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
              </>
            ) : (
              <SelectField
                label="Timeframe"
                value={candleOnlyTimeframe}
                onChange={(v) => setCandleOnlyTimeframe(v || '15m')}
                options={TIMEFRAME_OPTIONS}
                hint="Candle-only replay uses a manual timeframe for candle stepping without strategy evaluation."
              />
            )}
            <SelectField label="Risk Profile" value={form.riskProfileId} onChange={(v) => setForm((c) => ({ ...c, riskProfileId: v }))} options={reference.riskProfileOptions} required error={formErrors.riskProfileId} />
            <SelectField label="Execution Mode" value={form.executionMode} onChange={(v) => setForm((c) => ({ ...c, executionMode: v || 'MarketFill' }))} options={EXECUTION_MODE_OPTIONS} />
            <SelectField label="Initial Speed" value={form.speed} onChange={(v) => setForm((c) => ({ ...c, speed: v || 'ManualStep' }))} options={REPLAY_SPEED_OPTIONS} />
            <NumberField label="Initial Balance" value={form.initialBalance} onChange={(v) => setForm((c) => ({ ...c, initialBalance: v }))} required error={formErrors.initialBalance} />
            <AiScoringFields value={aiScoring} onChange={setAiScoring} />
            <NumberField label="Minimum Confidence Score" value={form.minConfidenceScore} onChange={(v) => setForm((c) => ({ ...c, minConfidenceScore: v }))} min={0} max={100} />
            <CheckboxField label="Show disabled strategies" checked={showDisabledStrategies} onChange={setShowDisabledStrategies} />
          </div>
          <DateRangeOnlySelector
            fromDate={form.fromUtc}
            toDate={form.toUtc}
            onChange={({ fromDate, toDate }) => setForm((c) => ({ ...c, fromUtc: fromDate, toUtc: toDate }))}
            required
            errors={{ fromDate: formErrors.fromUtc, toDate: formErrors.toUtc }}
          />
          <FormActions>
            <button type="button" onClick={() => void createSession()} className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950">Create Session</button>
            {replayMode === 'StrategyReplay' ? (
              <>
                <button type="button" onClick={() => void askAiAdvisor()} className="rounded-lg border border-slate-700 px-4 py-2 text-sm text-slate-200">
                  {advisorLoading ? 'Asking AI…' : 'Ask AI Setup Advisor'}
                </button>
                {advisor ? (
                  <button type="button" onClick={applyAiSuggestions} className="rounded-lg border border-emerald-700 px-4 py-2 text-sm text-emerald-200">
                    Apply AI Suggestions
                  </button>
                ) : null}
              </>
            ) : null}
          </FormActions>
          {advisor ? (
            <div className="mt-3 rounded-lg border border-slate-800 p-3 text-xs text-slate-300">
              <p className="font-medium text-slate-100">{advisor.summary}</p>
              <p>Required timeframes: {advisor.requiredTimeframes.join(', ')}</p>
            </div>
          ) : null}
        </FormPanel>
      ) : null}

      {sessions.loading ? <LoadingState /> : null}
      {sessions.error ? <ErrorState message={sessions.error} onRetry={sessions.reload} /> : null}
      {!sessions.loading && (sessions.data?.items.length ?? 0) === 0 ? (
        <EmptyState title="No replay sessions" description="Create a replay session to begin historical debugging." />
      ) : (
        <DataTable
          columns={[
            { key: 'name', header: 'Name', render: (row) => row.name },
            { key: 'status', header: 'Status', render: (row) => <StatusPill status={String(row.status)} /> },
            { key: 'symbol', header: 'Symbol', render: (row) => row.symbol ?? symbolLabel(reference.allSymbols, row.symbolId, reference.exchanges) },
            { key: 'exchange', header: 'Exchange', render: (row) => exchangeLabel(reference.exchanges, row.exchangeId) },
            { key: 'frame', header: 'Frame', render: (row) => `${row.currentFrameIndex ?? 0}/${row.totalFrames ?? 0}` },
            { key: 'created', header: 'Created', render: (row) => formatDate(row.createdAtUtc) },
            { key: 'view', header: '', render: (row) => <Link to={`/replay/${row.id}`} className="text-xs underline">Open</Link> },
          ]}
          rows={sessions.data?.items ?? []}
        />
      )}
    </div>
  );
}
