import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { PageHeader } from '@/components/common/PageHeader';
import { SimulationBanner } from '@/components/common/SimulationBanner';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { EmptyState } from '@/components/common/EmptyState';
import { ConfirmDialog } from '@/components/common/ConfirmDialog';
import { DataTable } from '@/components/common/DataTable';
import { FormPanel } from '@/components/common/FormPanel';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { ValidationSummary } from '@/components/common/ValidationSummary';
import { StatusPill } from '@/components/common/StatusPill';
import { FormActions } from '@/components/forms/FormActions';
import { CheckboxField, DateField, MultiSelectField, NumberField, SelectField, TextField } from '@/components/forms/fields';
import { CURRENCY_OPTIONS, EXECUTION_MODE_OPTIONS, PAPER_MODE_OPTIONS } from '@/constants/tradingOptions';
import { marketSituationApi, type MarketSituation } from '@/api/marketSituationApi';
import { strategyRecommendationsApi, type StrategyRecommendationResponse } from '@/api/strategyRecommendationsApi';
import { formatDate, formatNumber } from '@/components/common/utils';
import { useAsync } from '@/hooks/useAsync';
import { useReferenceData } from '@/hooks/useReferenceData';
import { useShowDisabledStrategies } from '@/hooks/useSessionPolling';
import { useRole } from '@/hooks/useRole';
import { paperTradingApi } from '@/api/paperTradingApi';
import { aiApi, type AiSetupAdvisorResponse } from '@/api/aiApi';
import { exchangeLabel, paperAccountLabel } from '@/utils/referenceLookups';
import { parseApiClientError } from '@/utils/apiError';
import { buildUtcRange } from '@/utils/formHelpers';
import { requireNumber, requireNumberArray, requireStringArray } from '@/utils/numbers';
import { getPaperSessionActions, paperSessionActionLabel, validateHistoricalPaperDates, validateRequired } from '@/utils/formValidation';
import { ExchangeSymbolSelector } from '@/components/strategies/ExchangeSymbolSelector';
import {
  getResolvedTimeframesForForm,
  StrategyAwareTimeframeSelector,
  type TimeframeMode,
} from '@/components/strategies/StrategyAwareTimeframeSelector';
import { ParameterSetMeta, StrategyParameterSetSelector } from '@/components/strategies/StrategyParameterSetSelector';

export function PaperTradingPage() {
  const { canEdit } = useRole();
  const [resetAccountId, setResetAccountId] = useState<number | null>(null);
  const [formErrors, setFormErrors] = useState<Record<string, string>>({});
  const [actionError, setActionError] = useState<string | null>(null);
  const [advisor, setAdvisor] = useState<AiSetupAdvisorResponse | null>(null);
  const [advisorLoading, setAdvisorLoading] = useState(false);
  const [accountName, setAccountName] = useState('Paper Account');
  const [initialBalance, setInitialBalance] = useState<number | ''>(10000);
  const [currency, setCurrency] = useState('USDT');
  const [marketSituation, setMarketSituation] = useState<MarketSituation | null>(null);
  const [recommendations, setRecommendations] = useState<StrategyRecommendationResponse | null>(null);
  const [analyzingMarket, setAnalyzingMarket] = useState(false);
  const [timeframeMode, setTimeframeMode] = useState<TimeframeMode>('StrategyDefault');
  const [customTimeframes, setCustomTimeframes] = useState<string[]>([]);
  const [parameterSetId, setParameterSetId] = useState<number | ''>('');
  const [sessionForm, setSessionForm] = useState({
    name: 'Paper Session',
    paperAccountId: '' as number | '',
    exchangeId: '' as number | '',
    symbolIds: [] as number[],
    mode: 'HistoricalPaper',
    fromUtc: '',
    toUtc: '',
    riskProfileId: '' as number | '',
    strategyIds: [] as number[],
    executionMode: 'MarketFill',
    makerFeeRate: 0.0002 as number | '',
    takerFeeRate: 0.0005 as number | '',
    orderExpiryCandles: 3 as number | '',
    useAiScoring: false,
    minConfidenceScore: 80 as number | '',
  });

  const reference = useReferenceData(sessionForm.exchangeId || null);
  const selectedStrategies = useMemo(
    () => reference.strategies.filter((strategy) => sessionForm.strategyIds.includes(strategy.id)),
    [reference.strategies, sessionForm.strategyIds],
  );
  const resolvedTimeframes = useMemo(
    () => getResolvedTimeframesForForm(selectedStrategies, timeframeMode, customTimeframes),
    [selectedStrategies, timeframeMode, customTimeframes],
  );
  const { showDisabledStrategies, setShowDisabledStrategies } = useShowDisabledStrategies();
  const sessions = useAsync(() => paperTradingApi.listSessions({ page: 1, pageSize: 50 }), []);

  async function createAccount() {
    if (!canEdit) return;
    const errors: Record<string, string> = {};
    if (validateRequired(accountName.trim(), 'Account name is required.')) errors.accountName = 'Account name is required.';
    if (initialBalance === '' || Number(initialBalance) <= 0) errors.initialBalance = 'Initial balance must be greater than zero.';
    setFormErrors(errors);
    if (Object.keys(errors).length) return;
    setActionError(null);
    try {
      await paperTradingApi.createAccount({ name: accountName.trim(), initialBalance: Number(initialBalance), currency });
      reference.reloadAll();
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  function validateSessionForm() {
    const errors: Record<string, string> = {};
    if (validateRequired(sessionForm.name.trim(), 'Session name is required.')) errors.name = 'Session name is required.';
    if (!sessionForm.paperAccountId) errors.paperAccountId = 'Paper account is required.';
    if (!sessionForm.exchangeId) errors.exchangeId = 'Exchange is required.';
    if (!sessionForm.symbolIds.length) errors.symbolIds = 'Select at least one symbol.';
    if (!resolvedTimeframes.length) errors.timeframes = 'Select at least one timeframe.';
    if (!sessionForm.riskProfileId) errors.riskProfileId = 'Risk profile is required.';
    if (!sessionForm.strategyIds.length) errors.strategyIds = 'Select at least one strategy.';
    Object.assign(errors, validateHistoricalPaperDates(sessionForm.mode, sessionForm.fromUtc, sessionForm.toUtc));
    setFormErrors(errors);
    return Object.keys(errors).length === 0;
  }

  async function analyzeCurrentMarket() {
    if (!sessionForm.exchangeId || !sessionForm.symbolIds[0] || !resolvedTimeframes[0]) return;
    setAnalyzingMarket(true);
    setActionError(null);
    try {
      const [situation, recs] = await Promise.all([
        marketSituationApi.getCurrent({
          exchangeId: requireNumber(sessionForm.exchangeId, 'Exchange'),
          symbolId: sessionForm.symbolIds[0],
          timeframe: resolvedTimeframes[0],
        }),
        strategyRecommendationsApi.getCurrent({
          exchangeId: requireNumber(sessionForm.exchangeId, 'Exchange'),
          symbolId: sessionForm.symbolIds[0],
          timeframe: resolvedTimeframes[0],
          mode: 'LivePaper',
        }),
      ]);
      setMarketSituation(situation);
      setRecommendations(recs);
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    } finally {
      setAnalyzingMarket(false);
    }
  }

  function useRecommendedStrategies() {
    if (!recommendations) return;
    setSessionForm((current) => ({
      ...current,
      strategyIds: recommendations.selectedByDefaultStrategyIds,
    }));
  }

  async function createSession() {
    if (!canEdit || !validateSessionForm()) return;
    setActionError(null);
    const isLive = sessionForm.mode === 'LivePaper';
    const range = isLive ? { fromUtc: undefined, toUtc: undefined } : buildUtcRange(sessionForm.fromUtc, sessionForm.toUtc);
    try {
      await paperTradingApi.createSession({
        name: sessionForm.name.trim(),
        paperAccountId: requireNumber(sessionForm.paperAccountId, 'Paper account'),
        exchangeId: requireNumber(sessionForm.exchangeId, 'Exchange'),
        symbolIds: requireNumberArray(sessionForm.symbolIds, 'Symbols'),
        timeframes: requireStringArray(resolvedTimeframes, 'Timeframes'),
        mode: sessionForm.mode,
        fromUtc: range.fromUtc,
        toUtc: range.toUtc,
        riskProfileId: requireNumber(sessionForm.riskProfileId, 'Risk profile'),
        strategyIds: requireNumberArray(sessionForm.strategyIds, 'Strategies'),
        parameterSetId: parameterSetId === '' ? undefined : Number(parameterSetId),
        executionMode: sessionForm.executionMode,
        makerFeeRate: requireNumber(sessionForm.makerFeeRate, 'Maker fee'),
        takerFeeRate: requireNumber(sessionForm.takerFeeRate, 'Taker fee'),
        orderExpiryCandles: requireNumber(sessionForm.orderExpiryCandles, 'Order expiry'),
        useAiScoring: sessionForm.useAiScoring,
        minConfidenceScore: requireNumber(sessionForm.minConfidenceScore, 'Minimum confidence'),
      });
      sessions.reload();
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  async function askAiAdvisor() {
    if (!sessionForm.symbolIds.length || !sessionForm.strategyIds.length) {
      setActionError('Select symbols and strategies before asking AI advisor.');
      return;
    }

    setAdvisorLoading(true);
    setActionError(null);
    try {
      const response = await aiApi.setupAdvisor({
        mode: sessionForm.mode === 'LivePaper' ? 'LivePaper' : 'HistoricalPaper',
        symbolIds: sessionForm.symbolIds,
        strategyIds: sessionForm.strategyIds,
        fromDate: sessionForm.fromUtc || undefined,
        toDate: sessionForm.toUtc || undefined,
        riskProfileId: sessionForm.riskProfileId === '' ? undefined : Number(sessionForm.riskProfileId),
        executionMode: sessionForm.executionMode,
        useAiScoring: sessionForm.useAiScoring,
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
    setSessionForm((current) => ({
      ...current,
      strategyIds: advisor.recommendedStrategies.length > 0 ? advisor.recommendedStrategies : current.strategyIds,
    }));
    if (executionTimeframes.length > 0) {
      setTimeframeMode('Custom');
      setCustomTimeframes(executionTimeframes);
    }
  }

  async function runSessionAction(sessionId: number, action: 'start' | 'pause' | 'resume' | 'stop') {
    if (!canEdit) return;
    try {
      if (action === 'start') await paperTradingApi.startSession(sessionId);
      if (action === 'pause') await paperTradingApi.pauseSession(sessionId);
      if (action === 'resume') await paperTradingApi.resumeSession(sessionId);
      if (action === 'stop') await paperTradingApi.stopSession(sessionId);
      sessions.reload();
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  return (
    <div>
      <PageHeader title="Paper Trading" description="Simulated accounts and paper sessions." />
      <SimulationBanner message="Paper trading is simulated. No real exchange orders are placed." />
      <ApiErrorAlert message={actionError} />
      <ValidationSummary errors={formErrors} />

      {canEdit ? (
        <>
          <FormPanel title="Create Paper Account" description="Start a new simulated account.">
            <div className="grid gap-4 md:grid-cols-3">
              <TextField label="Account Name" value={accountName} onChange={setAccountName} required error={formErrors.accountName} />
              <NumberField label="Initial Balance" value={initialBalance} onChange={setInitialBalance} required error={formErrors.initialBalance} />
              <SelectField label="Currency" value={currency} onChange={setCurrency} options={CURRENCY_OPTIONS} />
            </div>
            <FormActions><button type="button" onClick={() => void createAccount()} className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950">Create Account</button></FormActions>
          </FormPanel>

          <FormPanel title="Create Paper Session" description="Configure a simulated session.">
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              <TextField label="Session Name" value={sessionForm.name} onChange={(v) => setSessionForm((c) => ({ ...c, name: v }))} required error={formErrors.name} />
              <SelectField label="Paper Account" value={sessionForm.paperAccountId} onChange={(v) => setSessionForm((c) => ({ ...c, paperAccountId: v }))} options={reference.paperAccountOptions} required error={formErrors.paperAccountId} />
              <SelectField label="Paper Mode" value={sessionForm.mode} onChange={(v) => setSessionForm((c) => ({ ...c, mode: v || 'HistoricalPaper' }))} options={PAPER_MODE_OPTIONS} />
              <ExchangeSymbolSelector
                selectedExchangeId={sessionForm.exchangeId}
                selectedSymbolIds={sessionForm.symbolIds}
                onExchangeChange={(exchangeId) => setSessionForm((c) => ({ ...c, exchangeId, symbolIds: [] }))}
                onSymbolsChange={(symbolIds) => setSessionForm((c) => ({ ...c, symbolIds }))}
                required
                exchangeError={formErrors.exchangeId}
                symbolsError={formErrors.symbolIds}
              />
              <MultiSelectField label="Strategies" values={sessionForm.strategyIds} onChange={(v) => setSessionForm((c) => ({ ...c, strategyIds: v }))} options={reference.buildStrategyOptions(showDisabledStrategies)} required error={formErrors.strategyIds} />
              <StrategyAwareTimeframeSelector
                strategies={selectedStrategies}
                timeframeMode={timeframeMode}
                customTimeframes={customTimeframes}
                onTimeframeModeChange={setTimeframeMode}
                onCustomTimeframesChange={setCustomTimeframes}
                error={formErrors.timeframes}
              />
              <StrategyParameterSetSelector
                strategyCode={sessionForm.strategyIds.length === 1 ? selectedStrategies[0]?.code : undefined}
                symbolId={sessionForm.symbolIds.length === 1 ? sessionForm.symbolIds[0] : undefined}
                timeframe={resolvedTimeframes.length === 1 ? resolvedTimeframes[0] : undefined}
                selectedParameterSetId={parameterSetId}
                onChange={setParameterSetId}
                requiredForLivePaper={sessionForm.mode === 'LivePaper'}
              />
              <ParameterSetMeta parameterSetId={parameterSetId} />
              <SelectField label="Risk Profile" value={sessionForm.riskProfileId} onChange={(v) => setSessionForm((c) => ({ ...c, riskProfileId: v }))} options={reference.riskProfileOptions} required error={formErrors.riskProfileId} />
              <CheckboxField label="Show disabled strategies" checked={showDisabledStrategies} onChange={setShowDisabledStrategies} />
              <SelectField label="Execution Mode" value={sessionForm.executionMode} onChange={(v) => setSessionForm((c) => ({ ...c, executionMode: v || 'MarketFill' }))} options={EXECUTION_MODE_OPTIONS} />
              {sessionForm.mode === 'HistoricalPaper' ? (
                <>
                  <DateField label="From Date (UTC)" value={sessionForm.fromUtc} onChange={(v) => setSessionForm((c) => ({ ...c, fromUtc: v }))} required error={formErrors.fromUtc} />
                  <DateField label="To Date (UTC)" value={sessionForm.toUtc} onChange={(v) => setSessionForm((c) => ({ ...c, toUtc: v }))} required error={formErrors.toUtc} />
                </>
              ) : (
                <div className="md:col-span-2 rounded-lg border border-amber-900/50 bg-amber-950/20 p-3 text-sm text-amber-100">
                  Live Paper uses real-time public market data but simulated orders only. No real exchange orders are placed.
                </div>
              )}
              <NumberField label="Maker Fee Rate" value={sessionForm.makerFeeRate} onChange={(v) => setSessionForm((c) => ({ ...c, makerFeeRate: v }))} step={0.0001} />
              <NumberField label="Taker Fee Rate" value={sessionForm.takerFeeRate} onChange={(v) => setSessionForm((c) => ({ ...c, takerFeeRate: v }))} step={0.0001} />
              <NumberField label="Order Expiry (candles)" value={sessionForm.orderExpiryCandles} onChange={(v) => setSessionForm((c) => ({ ...c, orderExpiryCandles: v }))} />
              <CheckboxField label="Use AI scoring" checked={sessionForm.useAiScoring} onChange={(v) => setSessionForm((c) => ({ ...c, useAiScoring: v }))} />
              <NumberField label="Minimum Confidence Score" value={sessionForm.minConfidenceScore} onChange={(v) => setSessionForm((c) => ({ ...c, minConfidenceScore: v }))} min={0} max={100} />
            </div>
            {sessionForm.mode === 'LivePaper' ? (
              <div className="mt-4 space-y-4">
                <FormActions>
                  <button type="button" onClick={() => void analyzeCurrentMarket()} className="rounded-lg border border-slate-700 px-4 py-2 text-sm text-slate-200">
                    {analyzingMarket ? 'Analyzing…' : 'Analyze Current Market'}
                  </button>
                  {recommendations && !recommendations.warning && marketSituation?.marketRegime !== 'Unknown' ? (
                    <button type="button" onClick={useRecommendedStrategies} className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950">
                      Use Recommended Strategies
                    </button>
                  ) : null}
                </FormActions>
                {marketSituation ? (
                  <div className="rounded-lg border border-slate-800 p-4 text-sm text-slate-300">
                    <p className="font-medium text-slate-100">Market Situation</p>
                    <p className="mt-1">{marketSituation.summary}</p>
                    <p className="mt-2 text-xs text-slate-500">
                      {marketSituation.marketRegime} · {marketSituation.trendDirection} · {marketSituation.volatilityState}
                    </p>
                    <p className="mt-2 text-xs text-slate-500">
                      Data source: {marketSituation.dataSource.replace(/([A-Z])/g, ' $1').trim()} · Candles used: {marketSituation.candleCountUsed} · Indicators: {marketSituation.indicatorsAvailable ? 'available' : 'unavailable'}
                    </p>
                    {marketSituation.latestCandleTimeUtc ? (
                      <p className="mt-1 text-xs text-slate-500">Latest candle: {formatDate(marketSituation.latestCandleTimeUtc)}</p>
                    ) : null}
                  </div>
                ) : null}
                {recommendations ? (
                  <div className="rounded-lg border border-slate-800 p-4">
                    <p className="mb-2 text-sm font-medium text-slate-100">Recommended Strategies</p>
                    {recommendations.warning ? (
                      <p className="mb-2 text-sm text-amber-200">{recommendations.warning}</p>
                    ) : null}
                    <div className="space-y-2">
                      {recommendations.recommendedStrategies.filter((item) => item.recommended).map((item) => (
                        <div key={item.strategyId} className="text-sm text-slate-300">
                          <span className="font-medium text-slate-100">{item.strategyName}</span> ({item.suitabilityScore}) — {item.reason}
                        </div>
                      ))}
                    </div>
                  </div>
                ) : null}
              </div>
            ) : null}
            <FormActions>
              <button type="button" onClick={() => void createSession()} className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950">Create Session</button>
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
        </>
      ) : null}

      <section className="mb-6">
        <h2 className="mb-3 text-sm font-medium text-slate-300">Paper Accounts</h2>
        {(reference.paperAccounts.length ?? 0) === 0 && !reference.loading ? (
          <EmptyState title="No paper accounts" description="Create a paper account to start simulated trading." />
        ) : (
          <DataTable
            columns={[
              { key: 'name', header: 'Name', render: (row) => row.name },
              { key: 'equity', header: 'Equity', render: (row) => formatNumber(row.currentEquity) },
              { key: 'view', header: '', render: (row) => <Link to={`/paper-trading/accounts/${row.id}`} className="text-xs underline">View</Link> },
              { key: 'reset', header: '', render: (row) => canEdit ? <button type="button" onClick={() => setResetAccountId(row.id)} className="text-xs underline">Reset</button> : null },
            ]}
            rows={reference.paperAccounts}
          />
        )}
      </section>

      <section>
        <h2 className="mb-3 text-sm font-medium text-slate-300">Paper Sessions</h2>
        {sessions.loading ? <LoadingState /> : null}
        {sessions.error ? <ErrorState message={sessions.error} onRetry={sessions.reload} /> : null}
        <DataTable
          columns={[
            { key: 'name', header: 'Name', render: (row) => row.name },
            { key: 'account', header: 'Account', render: (row) => paperAccountLabel(reference.paperAccounts, row.paperAccountId) },
            { key: 'mode', header: 'Mode', render: (row) => row.mode },
            { key: 'status', header: 'Status', render: (row) => <StatusPill status={String(row.status)} /> },
            { key: 'exchange', header: 'Exchange', render: (row) => exchangeLabel(reference.exchanges, row.exchangeId) },
            { key: 'started', header: 'Started At', render: (row) => formatDate(row.startedAtUtc) },
            { key: 'view', header: '', render: (row) => <Link to={`/paper-trading/sessions/${row.id}`} className="text-xs underline">View</Link> },
            { key: 'actions', header: 'Actions', render: (row) => canEdit ? getPaperSessionActions(row.status).map((action) => (
              <button key={action} type="button" onClick={() => void runSessionAction(row.id, action)} className="mr-2 text-xs underline">{paperSessionActionLabel(action)}</button>
            )) : null },
          ]}
          rows={sessions.data?.items ?? []}
        />
      </section>

      <ConfirmDialog open={resetAccountId !== null} title="Reset Paper Account" message="This resets simulated balances and positions only." confirmLabel="Reset Account" onConfirm={() => void (async () => {
        if (!resetAccountId) return;
        try { await paperTradingApi.resetAccount(resetAccountId); reference.reloadAll(); setResetAccountId(null); } catch (e) { setActionError(parseApiClientError(e).message); setResetAccountId(null); }
      })()} onCancel={() => setResetAccountId(null)} />
    </div>
  );
}
