import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { PageHeader } from '@/components/common/PageHeader';
import { SimulationBanner } from '@/components/common/SimulationBanner';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { KeyValueGrid } from '@/components/common/KeyValueGrid';
import { Badge } from '@/components/common/Badge';
import { DataTable } from '@/components/common/DataTable';
import { formatDate, formatNumber, dedupeFailureReasons } from '@/components/common/utils';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { createExport, getExportDownloadUrl } from '@/api/exportsApi';
import {
  validationLabApi,
  type ValidationCandidateQuery,
  type ValidationExperimentDetail,
  type ValidationLayerType,
  type ValidationParameterTrial,
  type ValidationSegmentClassification,
  type ValidationSegmentResult,
  type ValidationTrainingProgress,
} from '@/api/validationLabApi';
import { parseApiClientError } from '@/utils/apiError';
import { useCandidatePage } from '@/hooks/useCandidatePage';
import { MetricCard } from '@/pages/validationLab/MetricCard';
import { SplitTimeline } from '@/pages/validationLab/SplitTimeline';
import { JsonBlock } from '@/pages/validationLab/JsonBlock';
import { SupersededBanner } from '@/pages/validationLab/SupersededBanner';
import { ExportVerificationPanel } from '@/pages/validationLab/ExportVerificationPanel';
import {
  ACTIVE_STATUSES,
  RESUMABLE_STATUSES,
  asRecord,
  buildValidationCandidateQuery,
  computeExperimentActionAvailability,
  formatExperimentVerdict,
  isInsufficientSample,
  isLegacyMetrics,
  pickLayer,
  tryParseJson,
  verdictTone,
} from '@/pages/validationLab/validationLabDetailHelpers';

type DetailTab =
  | 'overview'
  | 'split'
  | 'training'
  | 'frozen'
  | 'holdout'
  | 'layers'
  | 'confidence'
  | 'risk'
  | 'regime'
  | 'candidates'
  | 'diagnostics'
  | 'audit';

const TABS: { id: DetailTab; label: string }[] = [
  { id: 'overview', label: 'Overview' },
  { id: 'split', label: 'Split & Data' },
  { id: 'training', label: 'Training Search' },
  { id: 'frozen', label: 'Frozen Configuration' },
  { id: 'holdout', label: 'Holdout Validation' },
  { id: 'layers', label: 'Layer Comparison' },
  { id: 'confidence', label: 'Confidence Analysis' },
  { id: 'risk', label: 'Risk Analysis' },
  { id: 'regime', label: 'Regime Comparison' },
  { id: 'candidates', label: 'Candidates' },
  { id: 'diagnostics', label: 'Diagnostics' },
  { id: 'audit', label: 'Audit & Exports' },
];

export function ValidationLabExperimentDetailPage() {
  const { experimentId = '' } = useParams();
  const navigate = useNavigate();
  const id = Number(experimentId);

  const [detail, setDetail] = useState<ValidationExperimentDetail | null>(null);
  const [trials, setTrials] = useState<ValidationParameterTrial[]>([]);
  const [trainingProgress, setTrainingProgress] = useState<ValidationTrainingProgress | null>(null);
  const [comparison, setComparison] = useState<Record<string, unknown> | null>(null);
  const [confidence, setConfidence] = useState<Record<string, unknown> | null>(null);
  const [risk, setRisk] = useState<Record<string, unknown> | null>(null);
  const [diagnostics, setDiagnostics] = useState<Record<string, unknown> | null>(null);
  const [candidateSegment, setCandidateSegment] = useState<ValidationSegmentClassification | 'CrossSegmentOverlap'>('Training');
  const [candidateLayer, setCandidateLayer] = useState<ValidationLayerType>('RawStrategy');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [actionBusy, setActionBusy] = useState(false);
  const [activeTab, setActiveTab] = useState<DetailTab>('overview');

  const revealed = detail?.validationRevealStatus === 'Revealed';

  const load = useCallback(async () => {
    if (!Number.isFinite(id) || id <= 0) {
      setError('Invalid experiment id.');
      setLoading(false);
      return;
    }
    try {
      const res = await validationLabApi.getExperiment(id);
      setDetail(res ?? null);
      setError(null);
      if (
        res
        && (
          RESUMABLE_STATUSES.has(res.status)
          || res.status === 'TrainingRunning'
          || res.status === 'TrainingResumed'
          || res.status === 'ResumePreparing'
          || res.status === 'TrainingInterrupted'
        )
      ) {
        validationLabApi
          .getTrainingProgress(id)
          .then((progress) => setTrainingProgress(progress ?? null))
          .catch(() => setTrainingProgress(null));
      } else {
        setTrainingProgress(null);
      }
    } catch (err) {
      setError(parseApiClientError(err).message);
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    if (!detail) return;
    const timer = setInterval(() => {
      if (ACTIVE_STATUSES.has(detail.status)) {
        load();
      }
    }, 3000);
    return () => clearInterval(timer);
  }, [detail?.status, load, detail]);

  useEffect(() => {
    if (!detail || activeTab !== 'training') return;
    validationLabApi
      .getTrainingTrials(id)
      .then((data) => setTrials(data ?? []))
      .catch(() => setTrials([]));
  }, [activeTab, detail, id]);

  useEffect(() => {
    if (!detail || !revealed) return;
    if (activeTab === 'layers' || activeTab === 'overview') {
      validationLabApi
        .getComparison(id)
        .then((data) => setComparison(data ?? null))
        .catch(() => setComparison(null));
    }
    if (activeTab === 'confidence') {
      validationLabApi
        .getConfidenceAnalysis(id)
        .then((data) => setConfidence(data ?? null))
        .catch(() => setConfidence(null));
    }
    if (activeTab === 'risk') {
      validationLabApi
        .getRiskAnalysis(id)
        .then((data) => setRisk(data ?? null))
        .catch(() => setRisk(null));
    }
  }, [activeTab, detail, id, revealed]);

  useEffect(() => {
    if (!detail || activeTab !== 'diagnostics') return;
    validationLabApi
      .getDiagnostics(id)
      .then((data) => setDiagnostics(data ?? null))
      .catch(() => setDiagnostics(tryParseJson(detail.diagnosticsJson) as Record<string, unknown> | null));
  }, [activeTab, detail, id]);

  const candidateQuery = useMemo<ValidationCandidateQuery | null>(
    () => buildValidationCandidateQuery(candidateSegment, candidateLayer, revealed),
    [candidateSegment, candidateLayer, revealed],
  );

  const candidatesFetcher = useCallback(
    (signal: AbortSignal) => {
      if (!detail || activeTab !== 'candidates' || !candidateQuery) {
        return Promise.resolve<Record<string, unknown>[]>([]);
      }
      return validationLabApi.getCandidates(id, candidateQuery, signal).then((res) => res?.items ?? []);
    },
    [detail, activeTab, candidateQuery, id],
  );

  const candidatesResult = useCandidatePage<Record<string, unknown>[]>(candidatesFetcher, [
    detail,
    activeTab,
    candidateQuery,
    id,
  ]);
  const candidates = candidatesResult.error ? [] : candidatesResult.data ?? [];

  const runAction = async (action: () => Promise<unknown>, redirectId?: number) => {
    setActionBusy(true);
    setActionError(null);
    try {
      const result = await action();
      if (redirectId) {
        navigate(`/validation-lab/experiments/${redirectId}`);
        return;
      }
      if (result && typeof result === 'object' && 'id' in result && Number((result as { id: number }).id) !== id) {
        navigate(`/validation-lab/experiments/${(result as { id: number }).id}`);
        return;
      }
      await load();
    } catch (err) {
      setActionError(parseApiClientError(err).message);
    } finally {
      setActionBusy(false);
    }
  };

  const trainingRaw = useMemo(
    () => pickLayer(detail?.segmentResults, 'Training'),
    [detail?.segmentResults],
  );
  const validationRaw = useMemo(
    () => pickLayer(detail?.segmentResults, 'Validation'),
    [detail?.segmentResults],
  );

  const exclusivityReport = useMemo(() => {
    const parsed = tryParseJson(detail?.holdoutExclusivityJson);
    return asRecord(parsed);
  }, [detail?.holdoutExclusivityJson]);

  const reconciliationReport = useMemo(() => {
    const parsed = tryParseJson(detail?.candidateReconciliationJson);
    return asRecord(parsed);
  }, [detail?.candidateReconciliationJson]);

  const exportVerification = useMemo(() => {
    const parsed = tryParseJson(detail?.exportVerificationJson);
    return asRecord(parsed);
  }, [detail?.exportVerificationJson]);

  const trialPopulation = useMemo(() => {
    const parsed = tryParseJson(detail?.trialPopulationSummaryJson);
    return asRecord(parsed);
  }, [detail?.trialPopulationSummaryJson]);

  const exclusivityOverlaps = useMemo(() => {
    const fromExclusivity = exclusivityReport?.overlaps;
    if (Array.isArray(fromExclusivity)) return fromExclusivity as Record<string, unknown>[];
    const fromRecon = reconciliationReport?.overlaps;
    if (Array.isArray(fromRecon)) return fromRecon as Record<string, unknown>[];
    return [];
  }, [exclusivityReport, reconciliationReport]);

  if (loading) return <LoadingState />;
  if (error || !detail) return <ErrorState message={error ?? 'Experiment not found.'} onRetry={load} />;

  const insufficient = isInsufficientSample(detail.strategyRobustnessDecision);
  const legacyMetrics = isLegacyMetrics(detail.validationMetricsVersion);
  const exclusivityPolicy =
    (typeof exclusivityReport?.policy === 'string' && exclusivityReport.policy)
    || detail.holdoutExclusivityPolicyVersion
    || 'EarlierOccurrenceOwnsFingerprint';
  const exportManifest = asRecord(exportVerification?.manifest);
  const {
    canPrepare,
    canTrain,
    canResumeTraining,
    canFreeze,
    canValidate,
    canCloneOrRerun,
    zeroEligibleFailure,
  } = computeExperimentActionAvailability(detail);

  const layerResults = (detail.segmentResults ?? []).filter((s) =>
    revealed ? true : s.segmentType === 'Training',
  );

  return (
    <div>
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <PageHeader
          title={detail.name}
          description={`${detail.strategyCode} v${detail.strategyVersion} · ${detail.symbol} ${detail.timeframe}`}
        />
        <Link to="/validation-lab" className="rounded-lg border border-slate-700 px-3 py-1.5 text-sm">
          All experiments
        </Link>
      </div>

      <SimulationBanner
        message={
          revealed
            ? 'Holdout validation has been revealed. Results are final for this frozen configuration.'
            : 'Holdout validation performance stays hidden until the configuration is frozen and revealed.'
        }
      />

      <SupersededBanner
        supersessionStatus={detail.supersessionStatus}
        supersededByExperimentId={detail.supersededByExperimentId}
        supersessionReason={detail.supersessionReason}
      />

      {detail.isCanonical ? (
        <div className="mb-4 rounded-lg border border-emerald-800 bg-emerald-950/30 px-4 py-3 text-sm text-emerald-100">
          Canonical long-range Validation Laboratory experiment for infrastructure verification.
        </div>
      ) : null}

      {actionError ? <ApiErrorAlert message={actionError} /> : null}

      {zeroEligibleFailure ? (
        <div className="mb-4 rounded-lg border border-amber-800 bg-amber-950/40 px-4 py-3 text-sm text-amber-100">
          No training configuration passed the required guardrails. No configuration was selected and holdout
          validation was not run.
        </div>
      ) : null}

      {detail.status === 'Failed' || detail.status === 'TrainingInterrupted' ? (
        <div className="mb-4 rounded-lg border border-rose-800 bg-rose-950/40 px-4 py-3 text-sm text-rose-100">
          <div className="font-medium">
            {detail.status === 'TrainingInterrupted' ? 'Training interrupted' : 'Experiment failed'}
          </div>
          <div>{detail.errorMessage ?? 'Unknown failure.'}</div>
        </div>
      ) : null}

      {trainingProgress ? (
        <div className="mb-4 rounded-lg border border-slate-800 bg-slate-900/50 px-4 py-3 text-sm text-slate-200">
          <div className="mb-2 font-medium text-slate-100">Training progress (persisted)</div>
          <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
            <div>Completed: {trainingProgress.completedTrialCount} / {trainingProgress.requestedTrialCount}</div>
            <div>Pending: {trainingProgress.pendingTrialCount}</div>
            <div>Failed: {trainingProgress.failedTrialCount}</div>
            <div>Interrupted: {trainingProgress.interruptedTrialCount}</div>
            <div>
              Remaining:{' '}
              {Math.max(
                0,
                trainingProgress.generatedTrialCount
                  - trainingProgress.completedTrialCount
                  - trainingProgress.failedTrialCount,
              )}
            </div>
            <div>Progress: {formatNumber(trainingProgress.progressPercent)}%</div>
            {trainingProgress.activeTrialNumber ? (
              <div>Active trial: #{trainingProgress.activeTrialNumber}</div>
            ) : null}
            {trainingProgress.lastProgressAtUtc ? (
              <div>Last update: {formatDate(trainingProgress.lastProgressAtUtc)}</div>
            ) : null}
          </div>
        </div>
      ) : null}

      {detail.primaryQualificationLayer !== 'RawStrategy' || detail.primaryLayerWarning ? (
        <div className="mb-4 rounded-lg border border-amber-800 bg-amber-950/30 px-4 py-3 text-sm text-amber-100">
          <div className="font-medium">
            Primary qualification layer: {detail.primaryQualificationLayer}
          </div>
          <div>
            {detail.primaryLayerWarning
              ?? 'Non-raw primary layer selected. Treat gated sample results cautiously versus raw-strategy verdict.'}
          </div>
        </div>
      ) : null}

      {detail.holdoutReuseWarning?.repeatedHoldoutExposure ? (
        <div className="mb-4 rounded-lg border border-amber-800 bg-amber-950/30 px-4 py-3 text-sm text-amber-100">
          Repeated holdout exposure detected (overlap {formatNumber(detail.holdoutReuseWarning.overlapPercent)}%,
          risk {detail.holdoutReuseWarning.contaminationRisk}).
        </div>
      ) : null}

      <div
        className={`mb-4 rounded-xl border px-4 py-4 ${
          insufficient
            ? 'border-amber-600 bg-amber-950/40'
            : detail.strategyRobustnessDecision === 'Passed'
              || detail.strategyRobustnessDecision === 'ConditionallyPassed'
              ? 'border-emerald-700 bg-emerald-950/30'
              : detail.strategyRobustnessDecision
                ? 'border-rose-800 bg-rose-950/30'
                : 'border-slate-700 bg-slate-900/50'
        }`}
      >
        <div className="text-xs font-semibold uppercase tracking-widest text-slate-400">
          Strategy Robustness Verdict
        </div>
        <div className="mt-1 text-2xl font-semibold text-slate-50">
          {formatExperimentVerdict(detail.strategyRobustnessDecision)}
        </div>
        {detail.primaryFailureReason ? (
          <div className="mt-2 text-sm text-slate-300">{detail.primaryFailureReason}</div>
        ) : null}
        {dedupeFailureReasons(detail.failureReasonsJson).map((reason) => (
          <div key={reason} className="mt-1 text-sm text-slate-400">
            {reason}
          </div>
        ))}
        {detail.decisionExplanation ? (
          <div className="mt-1 text-sm text-slate-400">{detail.decisionExplanation}</div>
        ) : null}
        {insufficient ? (
          <div className="mt-2 text-sm text-amber-200">
            Sample size failure — do not interpret profitability metrics as a green pass.
          </div>
        ) : null}
      </div>

      <div className="mb-4 flex flex-wrap items-center gap-2">
        <Badge tone={detail.status === 'Completed' ? 'success' : detail.status === 'Failed' ? 'warning' : 'info'}>
          {detail.status}
        </Badge>
        <Badge tone={revealed ? 'success' : 'neutral'}>{detail.validationRevealStatus}</Badge>
        <Badge tone="info">{detail.experimentType}</Badge>
        {legacyMetrics ? <Badge tone="warning">Legacy Metrics</Badge> : null}
        {detail.validationLaboratoryReadinessStatus ? (
          <Badge
            tone={
              detail.validationLaboratoryReadinessStatus === 'Ready'
                ? 'success'
                : 'warning'
            }
          >
            {detail.validationLaboratoryReadinessStatus}
          </Badge>
        ) : null}
        <span className="text-sm text-slate-400">
          {detail.currentStage ?? '—'} ({formatNumber(detail.percentComplete)}%)
        </span>
      </div>

      <div className="mb-4 flex flex-wrap gap-2">
        {canPrepare ? (
          <button
            type="button"
            disabled={actionBusy}
            onClick={() => runAction(() => validationLabApi.prepareData(id))}
            className="rounded-lg border border-slate-700 px-3 py-1.5 text-sm disabled:opacity-50"
          >
            Prepare Data
          </button>
        ) : null}
        {canTrain ? (
          <button
            type="button"
            disabled={actionBusy}
            onClick={() => runAction(() => validationLabApi.runTraining(id))}
            className="rounded-lg border border-slate-700 px-3 py-1.5 text-sm disabled:opacity-50"
          >
            Run Training
          </button>
        ) : null}
        {canResumeTraining ? (
          <button
            type="button"
            disabled={actionBusy}
            onClick={() =>
              runAction(async () => {
                await validationLabApi.recoverTrials(id);
                return validationLabApi.resumeTraining(id);
              })
            }
            className="rounded-lg bg-amber-100 px-3 py-1.5 text-sm font-medium text-amber-950 disabled:opacity-50"
          >
            Resume Training
          </button>
        ) : null}
        {canFreeze ? (
          <button
            type="button"
            disabled={actionBusy}
            onClick={() => runAction(() => validationLabApi.freeze(id))}
            className="rounded-lg border border-slate-700 px-3 py-1.5 text-sm disabled:opacity-50"
          >
            Freeze Configuration
          </button>
        ) : null}
        {canValidate ? (
          <button
            type="button"
            disabled={actionBusy}
            onClick={() => runAction(() => validationLabApi.runValidation(id))}
            className="rounded-lg bg-slate-100 px-3 py-1.5 text-sm font-medium text-slate-950 disabled:opacity-50"
          >
            Run Validation
          </button>
        ) : null}
        {canCloneOrRerun ? (
          <>
            <button
              type="button"
              disabled={actionBusy}
              onClick={() =>
                runAction(async () => {
                  const cloned = await validationLabApi.clone(id);
                  return cloned;
                })
              }
              className="rounded-lg border border-slate-700 px-3 py-1.5 text-sm disabled:opacity-50"
            >
              Clone
            </button>
            <button
              type="button"
              disabled={actionBusy}
              onClick={() =>
                runAction(async () => {
                  const rerun = await validationLabApi.rerunExactly(id);
                  return rerun;
                })
              }
              className="rounded-lg border border-slate-700 px-3 py-1.5 text-sm disabled:opacity-50"
            >
              Re-run Exactly
            </button>
          </>
        ) : null}
      </div>

      <div className="mb-4 flex flex-wrap gap-2 border-b border-slate-800 pb-3">
        {TABS.map((tab) => (
          <button
            key={tab.id}
            type="button"
            onClick={() => setActiveTab(tab.id)}
            className={`rounded-lg px-3 py-1.5 text-sm ${
              activeTab === tab.id ? 'bg-slate-700 text-white' : 'text-slate-300'
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {activeTab === 'overview' ? (
        <div className="space-y-4">
          <KeyValueGrid
            items={[
              { label: 'Experiment ID', value: String(detail.id) },
              { label: 'Status', value: detail.status },
              { label: 'Reveal Status', value: detail.validationRevealStatus },
              { label: 'Verdict', value: <Badge tone={verdictTone(detail.strategyRobustnessDecision)}>{formatExperimentVerdict(detail.strategyRobustnessDecision)}</Badge> },
              {
                label: 'Metrics Version',
                value: (
                  <span className="inline-flex flex-wrap items-center gap-2">
                    <span>{detail.validationMetricsVersion || '—'}</span>
                    {legacyMetrics ? <Badge tone="warning">Legacy Metric Unit Warning</Badge> : null}
                  </span>
                ),
              },
              { label: 'Reconciliation', value: detail.candidateReconciliationStatus || '—' },
              { label: 'Leakage Audit', value: detail.leakageAuditStatus || '—' },
              { label: 'Export Verification', value: detail.exportVerificationStatus || 'NotRun' },
              {
                label: 'Selection Integrity',
                value: detail.selectionIntegrityStatus || 'NotEvaluated',
              },
              {
                label: 'Laboratory Readiness',
                value: detail.validationLaboratoryReadinessStatus || '—',
              },
              {
                label: 'Parameter Stability',
                value:
                  detail.parameterStabilityApplicability === 'NotApplicable' ? (
                    <Badge tone="info">NotApplicable</Badge>
                  ) : (
                    detail.parameterStabilityApplicability || '—'
                  ),
              },
              { label: 'Expectancy Metric', value: detail.expectancyMetric || 'NetExpectancyR' },
              { label: 'Requested Range', value: `${formatDate(detail.requestedStartUtc)} → ${formatDate(detail.requestedEndUtc)}` },
              { label: 'Split Ratio', value: `${formatNumber(detail.splitRatio * 100)}% / ${formatNumber((1 - detail.splitRatio) * 100)}%` },
              { label: 'Candle Fingerprint', value: detail.candleDataFingerprint || '—' },
              { label: 'Frozen At', value: formatDate(detail.frozenAtUtc) },
              { label: 'Revealed At', value: formatDate(detail.validationRevealedAtUtc) },
              { label: 'Boundary Censored', value: String(detail.boundaryCensoredCount) },
              {
                label: 'Cross-segment Overlaps',
                value: String(detail.crossSegmentOverlapCount ?? exclusivityOverlaps.length ?? 0),
              },
            ]}
          />

          {(exclusivityReport || reconciliationReport || detail.crossSegmentOverlapCount) ? (
            <div className="rounded-lg border border-slate-800 p-4">
              <h3 className="mb-2 text-sm font-semibold text-slate-200">Holdout exclusivity / reconciliation</h3>
              <KeyValueGrid
                items={[
                  { label: 'Exclusion policy', value: exclusivityPolicy },
                  {
                    label: 'Policy version',
                    value: detail.holdoutExclusivityPolicyVersion
                      || String(exclusivityReport?.policyVersion ?? '—'),
                  },
                  { label: 'Metric owner', value: 'Training' },
                  {
                    label: 'Overlap count',
                    value: String(
                      detail.crossSegmentOverlapCount
                        ?? exclusivityReport?.crossSegmentOverlapCount
                        ?? exclusivityOverlaps.length
                        ?? 0,
                    ),
                  },
                  {
                    label: 'Reconciliation status',
                    value: detail.candidateReconciliationStatus || '—',
                  },
                ]}
              />
              {exclusivityOverlaps.length > 0 ? (
                <div className="mt-3">
                  <div className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">
                    Overlaps (train / val candidate IDs)
                  </div>
                  <DataTable
                    columns={[
                      {
                        key: 'fp',
                        header: 'Fingerprint',
                        render: (row: Record<string, unknown>) =>
                          String(row.overlapFingerprint ?? row.setupFingerprint ?? '—').slice(0, 24),
                      },
                      {
                        key: 'train',
                        header: 'Train ID',
                        render: (row: Record<string, unknown>) =>
                          String(row.canonicalOccurrenceCandidateId ?? row.trainingCandidateId ?? '—'),
                      },
                      {
                        key: 'val',
                        header: 'Val ID',
                        render: (row: Record<string, unknown>) =>
                          String(row.duplicateOccurrenceCandidateId ?? row.validationCandidateId ?? '—'),
                      },
                      {
                        key: 'owner',
                        header: 'Metric owner',
                        render: (row: Record<string, unknown>) => String(row.metricOwner ?? 'Training'),
                      },
                    ]}
                    rows={exclusivityOverlaps.slice(0, 20)}
                    emptyMessage="No overlaps."
                  />
                </div>
              ) : (
                <p className="mt-2 text-sm text-slate-400">
                  No cross-segment fingerprint overlaps recorded.
                </p>
              )}
            </div>
          ) : null}

          <div className="grid gap-4 md:grid-cols-2">
            <MetricCard
              title="Training"
              closedTrades={trainingRaw?.closedTradeCount}
              expectancy={trainingRaw?.netExpectancyR}
              grossExpectancy={trainingRaw?.grossExpectancyR}
              profitFactor={trainingRaw?.profitFactor}
              netProfitFactor={trainingRaw?.netProfitFactor}
              grossProfitFactor={trainingRaw?.grossProfitFactor}
              netReturn={trainingRaw?.netReturnPercent}
              drawdown={trainingRaw?.maximumDrawdownPercent}
              grossProfit={trainingRaw?.grossProfit}
              grossLoss={trainingRaw?.grossLoss}
              netProfit={trainingRaw?.netProfit}
              netLoss={trainingRaw?.netLoss}
              insufficient={detail.strategyRobustnessDecision === 'FailedInsufficientTrainingSample'}
            />
            <MetricCard
              title="Validation"
              closedTrades={revealed ? validationRaw?.closedTradeCount : undefined}
              expectancy={revealed ? validationRaw?.netExpectancyR : undefined}
              grossExpectancy={revealed ? validationRaw?.grossExpectancyR : undefined}
              profitFactor={revealed ? validationRaw?.profitFactor : undefined}
              netProfitFactor={revealed ? validationRaw?.netProfitFactor : undefined}
              grossProfitFactor={revealed ? validationRaw?.grossProfitFactor : undefined}
              netReturn={revealed ? validationRaw?.netReturnPercent : undefined}
              drawdown={revealed ? validationRaw?.maximumDrawdownPercent : undefined}
              grossProfit={revealed ? validationRaw?.grossProfit : undefined}
              grossLoss={revealed ? validationRaw?.grossLoss : undefined}
              netProfit={revealed ? validationRaw?.netProfit : undefined}
              netLoss={revealed ? validationRaw?.netLoss : undefined}
              insufficient={revealed && detail.strategyRobustnessDecision === 'FailedInsufficientValidationSample'}
              hidden={!revealed}
            />
          </div>

          {!revealed ? (
            <div className="rounded-lg border border-slate-800 px-4 py-3 text-sm text-slate-300">
              <div className="font-medium text-slate-100">Validation window (pre-reveal)</div>
              <div>
                Range: {formatDate(detail.validationStartUtc)} → {formatDate(detail.validationEndUtc)}
              </div>
              <div>Validation candles: {detail.validationCandleCount}</div>
              <div>Training candles: {detail.trainingCandleCount}</div>
            </div>
          ) : null}

          {revealed && trainingRaw && validationRaw ? (
            <div className="rounded-lg border border-slate-800 p-4">
              <h3 className="mb-2 text-sm font-semibold text-slate-200">Degradation</h3>
              <KeyValueGrid
                items={[
                  {
                    label: 'Expectancy Δ',
                    value: formatNumber((validationRaw.netExpectancyR ?? 0) - (trainingRaw.netExpectancyR ?? 0)),
                  },
                  {
                    label: 'Profit Factor Δ',
                    value: formatNumber((validationRaw.profitFactor ?? 0) - (trainingRaw.profitFactor ?? 0)),
                  },
                  {
                    label: 'Drawdown Δ',
                    value: formatNumber(
                      (validationRaw.maximumDrawdownPercent ?? 0) - (trainingRaw.maximumDrawdownPercent ?? 0),
                    ),
                  },
                  {
                    label: 'Closed Trades Δ',
                    value: String((validationRaw.closedTradeCount ?? 0) - (trainingRaw.closedTradeCount ?? 0)),
                  },
                ]}
              />
            </div>
          ) : null}

          <div className="rounded-lg border border-slate-800 p-4">
            <h3 className="mb-2 text-sm font-semibold text-slate-200">Layer Results</h3>
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              {(['RawStrategy', 'ConfidenceQualified', 'RiskOnly', 'FullPipeline'] as ValidationLayerType[]).map(
                (layer) => {
                  const train = pickLayer(detail.segmentResults, 'Training', layer);
                  const val = revealed ? pickLayer(detail.segmentResults, 'Validation', layer) : undefined;
                  return (
                    <div key={layer} className="rounded-lg border border-slate-800 px-3 py-2 text-sm">
                      <div className="mb-1 font-medium text-slate-200">{layer}</div>
                      <div className="text-slate-400">
                        Train n={train?.closedTradeCount ?? '—'}
                        {train?.netExpectancyR != null ? ` · E=${formatNumber(train.netExpectancyR)}` : ''}
                      </div>
                      <div className="text-slate-400">
                        Val n={revealed ? (val?.closedTradeCount ?? '—') : 'hidden'}
                        {revealed && val?.netExpectancyR != null
                          ? ` · E=${formatNumber(val.netExpectancyR)}`
                          : ''}
                      </div>
                    </div>
                  );
                },
              )}
            </div>
          </div>
        </div>
      ) : null}

      {activeTab === 'split' ? (
        <div className="space-y-4">
          <SplitTimeline detail={detail} />
          <KeyValueGrid
            items={[
              { label: 'Algorithm', value: detail.splitAlgorithmVersion || '—' },
              { label: 'Total Eligible Candles', value: String(detail.totalEligibleCandleCount) },
              { label: 'Training Start', value: formatDate(detail.trainingStartUtc) },
              { label: 'Training End', value: formatDate(detail.trainingEndUtc) },
              { label: 'Validation Start', value: formatDate(detail.validationStartUtc) },
              { label: 'Validation End', value: formatDate(detail.validationEndUtc) },
              { label: 'Training Warmup Start', value: formatDate(detail.trainingWarmupStartUtc) },
              { label: 'Validation Warmup Start', value: formatDate(detail.validationWarmupStartUtc) },
              { label: 'Candle Fingerprint', value: detail.candleDataFingerprint || '—' },
            ]}
          />
          <JsonBlock title="Candle Data Snapshot" value={detail.candleDataSnapshotJson} />
          <JsonBlock title="Warmup Snapshot" value={detail.warmupSnapshotJson} />
        </div>
      ) : null}

      {activeTab === 'training' ? (
        <div className="space-y-4">
          <KeyValueGrid
            items={[
              { label: 'Maximum Trials', value: String(detail.maximumTrials) },
              { label: 'Deterministic Seed', value: String(detail.deterministicSeed) },
              { label: 'Training Lab Run', value: detail.trainingStrategyLabRunId ? String(detail.trainingStrategyLabRunId) : '—' },
              { label: 'Trials Loaded', value: String(trials.length) },
              { label: 'Terminal Trials', value: trialPopulation?.terminalTrialCount != null ? String(trialPopulation.terminalTrialCount) : '—' },
              {
                label: 'Eligible Trials',
                value: trialPopulation?.eligibleTrialCount != null
                  ? String(trialPopulation.eligibleTrialCount)
                  : trialPopulation?.completedEligibleCount != null
                    ? String(trialPopulation.completedEligibleCount)
                    : '—',
              },
              {
                label: 'Guardrail Rejected',
                value: trialPopulation?.guardrailRejectedTrialCount != null
                  ? String(trialPopulation.guardrailRejectedTrialCount)
                  : trialPopulation?.guardrailRejectedCount != null
                    ? String(trialPopulation.guardrailRejectedCount)
                    : '—',
              },
              {
                label: 'Selected Trial',
                value: detail.selectedTrialId ? `#${detail.selectedTrialNumber ?? detail.selectedTrialId}` : '—',
              },
              { label: 'Selection Integrity', value: detail.selectionIntegrityStatus || 'NotEvaluated' },
              {
                label: 'Failed / Interrupted',
                value: `${trialPopulation?.failedTrialCount ?? trialPopulation?.failedCount ?? '—'} / ${trialPopulation?.interruptedTrialCount ?? trialPopulation?.interruptedCount ?? '—'}`,
              },
              { label: 'Pending / Running', value: `${trialPopulation?.pendingCount ?? '—'} / ${trialPopulation?.runningCount ?? '—'}` },
              { label: 'Recovered from Strategy Lab', value: trialPopulation?.recoveredFromStrategyLabRunCount != null ? String(trialPopulation.recoveredFromStrategyLabRunCount) : '—' },
            ]}
          />
          <DataTable
            columns={[
              { key: 'trial', header: 'Trial', render: (row: ValidationParameterTrial) => row.trialNumber },
              { key: 'rank', header: 'Rank', render: (row: ValidationParameterTrial) => row.rank ?? '—' },
              { key: 'status', header: 'Status', render: (row: ValidationParameterTrial) => row.status },
              { key: 'closed', header: 'Closed (n)', render: (row: ValidationParameterTrial) => row.closedTradeCount },
              {
                key: 'exp',
                header: 'Expectancy R',
                render: (row: ValidationParameterTrial) =>
                  row.netExpectancyR == null ? '—' : formatNumber(row.netExpectancyR),
              },
              {
                key: 'pf',
                header: 'Profit Factor',
                render: (row: ValidationParameterTrial) =>
                  row.profitFactor == null ? '—' : formatNumber(row.profitFactor),
              },
              {
                key: 'score',
                header: 'Training Score',
                render: (row: ValidationParameterTrial) =>
                  row.trainingScore == null ? '—' : formatNumber(row.trainingScore),
              },
              { key: 'guard', header: 'Guardrail', render: (row: ValidationParameterTrial) => row.guardrailDecision },
              {
                key: 'fp',
                header: 'Fingerprint',
                render: (row: ValidationParameterTrial) => row.parameterFingerprint,
              },
            ]}
            rows={trials}
            emptyMessage="No training trials yet. Run Training after Prepare Data."
          />
          <JsonBlock title="Parameter Search Space" value={detail.parameterSearchSpaceSnapshotJson} />
          <JsonBlock title="Parameter Stability" value={detail.parameterStabilityJson} />
        </div>
      ) : null}

      {activeTab === 'frozen' ? (
        <div className="space-y-4">
          <KeyValueGrid
            items={[
              { label: 'Frozen At', value: formatDate(detail.frozenAtUtc) },
              { label: 'Selected Trial Fingerprint', value: detail.selectedTrialParameterFingerprint || '—' },
              { label: 'Frozen Parameter Fingerprint', value: detail.frozenParameterFingerprint || '—' },
              {
                label: 'Fingerprint Match',
                value:
                  detail.selectedTrialParameterFingerprint && detail.frozenParameterFingerprint
                    ? detail.selectedTrialParameterFingerprint === detail.frozenParameterFingerprint
                      ? 'Match'
                      : 'Mismatch'
                    : '—',
              },
              { label: 'Snapshot Validation', value: detail.frozenSnapshotValidationStatus || '—' },
              { label: 'Freeze Source', value: detail.freezeSource || '—' },
              { label: 'Strategy Fingerprint', value: detail.frozenStrategyFingerprint || '—' },
              { label: 'Reveal Status', value: detail.validationRevealStatus },
            ]}
          />
          <JsonBlock title="Frozen Strategy Parameters" value={detail.frozenStrategyParameterSnapshotJson} />
          <JsonBlock title="Frozen Confidence" value={detail.frozenConfidenceSnapshotJson} />
          <JsonBlock title="Frozen Risk" value={detail.frozenRiskSnapshotJson} />
          <JsonBlock title="Frozen Cost Model" value={detail.frozenCostModelSnapshotJson} />
          <JsonBlock title="Qualification Profile" value={detail.qualificationProfileSnapshotJson} />
          <JsonBlock title="Draft Configuration" value={detail.draftConfigurationJson} />
        </div>
      ) : null}

      {activeTab === 'holdout' ? (
        <div className="space-y-4">
          <KeyValueGrid
            items={[
              { label: 'Validation Range', value: `${formatDate(detail.validationStartUtc)} → ${formatDate(detail.validationEndUtc)}` },
              { label: 'Validation Candles', value: String(detail.validationCandleCount) },
              { label: 'Reveal Status', value: detail.validationRevealStatus },
              { label: 'Revealed At', value: formatDate(detail.validationRevealedAtUtc) },
              {
                label: 'Validation Lab Run',
                value: detail.validationStrategyLabRunId ? String(detail.validationStrategyLabRunId) : '—',
              },
            ]}
          />
          {!revealed ? (
            <div className="rounded-lg border border-slate-800 bg-slate-950/40 px-4 py-3 text-sm text-slate-300">
              Holdout performance is concealed until reveal. Only the validation date range and candle counts are shown.
            </div>
          ) : (
            <MetricCard
              title="Holdout Validation (Raw Strategy)"
              closedTrades={validationRaw?.closedTradeCount}
              expectancy={validationRaw?.netExpectancyR}
              grossExpectancy={validationRaw?.grossExpectancyR}
              profitFactor={validationRaw?.profitFactor}
              netProfitFactor={validationRaw?.netProfitFactor}
              grossProfitFactor={validationRaw?.grossProfitFactor}
              netReturn={validationRaw?.netReturnPercent}
              drawdown={validationRaw?.maximumDrawdownPercent}
              grossProfit={validationRaw?.grossProfit}
              grossLoss={validationRaw?.grossLoss}
              netProfit={validationRaw?.netProfit}
              netLoss={validationRaw?.netLoss}
              insufficient={detail.strategyRobustnessDecision === 'FailedInsufficientValidationSample'}
            />
          )}
        </div>
      ) : null}

      {activeTab === 'layers' ? (
        <div className="space-y-4">
          {!revealed ? (
            <div className="rounded-lg border border-slate-800 px-4 py-3 text-sm text-slate-400">
              Full training-vs-validation layer comparison requires reveal. Training layer rows are shown below.
            </div>
          ) : null}
          <DataTable
            columns={[
              { key: 'seg', header: 'Segment', render: (row: ValidationSegmentResult) => row.segmentType },
              { key: 'layer', header: 'Layer', render: (row: ValidationSegmentResult) => row.layerType },
              { key: 'candles', header: 'Candles', render: (row: ValidationSegmentResult) => row.candleCount },
              { key: 'cands', header: 'Candidates', render: (row: ValidationSegmentResult) => row.candidateCount },
              { key: 'closed', header: 'Closed (n)', render: (row: ValidationSegmentResult) => row.closedTradeCount },
              {
                key: 'exp',
                header: 'Expectancy R',
                render: (row: ValidationSegmentResult) =>
                  row.segmentType === 'Validation' && !revealed
                    ? 'hidden'
                    : row.netExpectancyR == null
                      ? '—'
                      : formatNumber(row.netExpectancyR),
              },
              {
                key: 'pf',
                header: 'Profit Factor',
                render: (row: ValidationSegmentResult) =>
                  row.segmentType === 'Validation' && !revealed
                    ? 'hidden'
                    : row.profitFactor == null
                      ? '—'
                      : formatNumber(row.profitFactor),
              },
              {
                key: 'dd',
                header: 'Drawdown %',
                render: (row: ValidationSegmentResult) =>
                  row.segmentType === 'Validation' && !revealed
                    ? 'hidden'
                    : row.maximumDrawdownPercent == null
                      ? '—'
                      : formatNumber(row.maximumDrawdownPercent),
              },
            ]}
            rows={layerResults}
            emptyMessage="No segment results yet."
          />
          {revealed ? <JsonBlock title="Comparison Payload" value={JSON.stringify(comparison ?? tryParseJson(detail.comparisonJson), null, 2)} /> : null}
          <JsonBlock title="Overlay Results" value={detail.overlayResultsJson} />
        </div>
      ) : null}

      {activeTab === 'confidence' ? (
        <div className="space-y-4">
          {!revealed ? (
            <div className="rounded-lg border border-slate-800 px-4 py-3 text-sm text-slate-400">
              Confidence analysis on holdout is available after reveal.
            </div>
          ) : (
            <JsonBlock title="Confidence Analysis" value={JSON.stringify(confidence ?? {}, null, 2)} />
          )}
        </div>
      ) : null}

      {activeTab === 'risk' ? (
        <div className="space-y-4">
          {!revealed ? (
            <div className="rounded-lg border border-slate-800 px-4 py-3 text-sm text-slate-400">
              Risk analysis on holdout is available after reveal.
            </div>
          ) : (
            <JsonBlock title="Risk Analysis" value={JSON.stringify(risk ?? {}, null, 2)} />
          )}
        </div>
      ) : null}

      {activeTab === 'regime' ? (
        <div className="space-y-4">
          {!revealed ? (
            <div className="rounded-lg border border-slate-800 px-4 py-3 text-sm text-slate-400">
              Regime comparison requires revealed validation results.
            </div>
          ) : (
            <JsonBlock title="Regime Comparison" value={detail.regimeComparisonJson} />
          )}
        </div>
      ) : null}

      {activeTab === 'candidates' ? (
        <div className="space-y-4">
          <div className="flex flex-wrap gap-3">
            <SelectInline
              label="Segment"
              value={candidateSegment}
              onChange={(v) => setCandidateSegment(v as ValidationSegmentClassification | 'CrossSegmentOverlap')}
              options={['Training', 'Validation', 'BoundaryCensored', 'CrossSegmentOverlap']}
            />
            <SelectInline
              label="Layer"
              value={candidateLayer}
              onChange={(v) => setCandidateLayer(v as ValidationLayerType)}
              options={['RawStrategy', 'ConfidenceQualified', 'RiskOnly', 'FullPipeline']}
            />
          </div>
          {candidateSegment === 'Validation' && !revealed ? (
            <div className="rounded-lg border border-slate-800 px-4 py-3 text-sm text-slate-400">
              Validation candidates are unavailable until the holdout is revealed.
            </div>
          ) : (
            <DataTable
              columns={[
                {
                  key: 'id',
                  header: 'ID',
                  render: (row: Record<string, unknown>) => String(row.id ?? '—'),
                },
                {
                  key: 'dir',
                  header: 'Direction',
                  render: (row: Record<string, unknown>) => String(row.direction ?? '—'),
                },
                {
                  key: 'time',
                  header: 'Setup Time',
                  render: (row: Record<string, unknown>) =>
                    formatDate(typeof row.setupDetectedAtUtc === 'string' ? row.setupDetectedAtUtc : null),
                },
                {
                  key: 'outcome',
                  header: 'Outcome',
                  render: (row: Record<string, unknown>) => String(row.rawOutcomeStatus ?? '—'),
                },
                {
                  key: 'pnl',
                  header: 'Net PnL',
                  render: (row: Record<string, unknown>) =>
                    row.rawNetPnl == null ? '—' : formatNumber(Number(row.rawNetPnl)),
                },
                {
                  key: 'conf',
                  header: 'Confidence',
                  render: (row: Record<string, unknown>) =>
                    row.confidenceDecision == null ? '—' : String(row.confidenceDecision),
                },
                {
                  key: 'risk',
                  header: 'Risk',
                  render: (row: Record<string, unknown>) =>
                    row.riskDecision == null ? '—' : String(row.riskDecision),
                },
                {
                  key: 'exclusivity',
                  header: 'Exclusivity',
                  render: (row: Record<string, unknown>) => {
                    const classification = String(row.metricClassification ?? '');
                    const excluded =
                      classification === 'CrossSegmentOverlapExcludedFromValidation'
                      || row.portfolioMutationAllowed === false;
                    if (!excluded) return '—';
                    const reason =
                      typeof row.metricExclusionReason === 'string' && row.metricExclusionReason
                        ? row.metricExclusionReason
                        : 'Excluded from validation metrics (audit-only; portfolioMutationAllowed=false).';
                    return (
                      <span className="text-xs text-amber-200" title={reason}>
                        {reason}
                      </span>
                    );
                  },
                },
              ]}
              rows={candidates}
              emptyMessage="No candidates for this filter."
            />
          )}
        </div>
      ) : null}

      {activeTab === 'diagnostics' ? (
        <div className="space-y-4">
          <JsonBlock
            title="Diagnostics"
            value={JSON.stringify(diagnostics ?? tryParseJson(detail.diagnosticsJson) ?? [], null, 2)}
          />
          <JsonBlock title="Failure Reasons" value={detail.failureReasonsJson} />
          <JsonBlock title="Qualification Rule Results" value={detail.qualificationRuleResultsJson} />
        </div>
      ) : null}

      {activeTab === 'audit' ? (
        <div className="space-y-4 text-sm text-slate-300">
          <div className="rounded-lg border border-slate-800 px-4 py-3">
            <div className="mb-2 font-medium text-slate-100">Audit snapshot</div>
            <ul className="list-disc space-y-1 pl-5">
              <li>Experiment ID: {detail.id}</li>
              <li>
                Strategy / version: {detail.strategyCode} / {detail.strategyVersion}
              </li>
              <li>
                Metrics version: {detail.validationMetricsVersion || '—'}
                {legacyMetrics ? (
                  <>
                    {' '}
                    <Badge tone="warning">Legacy Metrics</Badge>
                  </>
                ) : null}
              </li>
              <li>Reconciliation: {detail.candidateReconciliationStatus || '—'}</li>
              <li>Leakage audit: {detail.leakageAuditStatus || '—'}</li>
              <li>Parameter stability: {detail.parameterStabilityApplicability || '—'}</li>
              <li>Holdout exclusivity policy: {detail.holdoutExclusivityPolicyVersion || exclusivityPolicy}</li>
              <li>Cross-segment overlaps: {detail.crossSegmentOverlapCount ?? 0}</li>
              <li>Metric consistency: {detail.metricConsistencyStatus || '—'}</li>
              <li>Export verification: {detail.exportVerificationStatus || 'NotRun'}</li>
              <li>Laboratory readiness: {detail.validationLaboratoryReadinessStatus || '—'}</li>
              <li>Data fingerprint: {detail.candleDataFingerprint || '—'}</li>
              <li>Frozen parameter fingerprint: {detail.frozenParameterFingerprint || '—'}</li>
              <li>Reveal status: {detail.validationRevealStatus}</li>
              <li>Final verdict: {formatExperimentVerdict(detail.strategyRobustnessDecision)}</li>
              <li>Created: {formatDate(detail.createdAtUtc)}</li>
              <li>Updated: {formatDate(detail.updatedAtUtc)}</li>
            </ul>
          </div>
          <ExportVerificationPanel
            status={detail.exportVerificationStatus}
            manifest={exportManifest}
            issues={Array.isArray(exportVerification?.issues) ? (exportVerification.issues as unknown[]) : undefined}
          />
          <div className="rounded-lg border border-slate-800 px-4 py-3">
            <div className="mb-2 font-medium text-slate-100">Exports</div>
            <p className="mb-3 text-slate-400">
              Validation result exports are blocked until reveal status is Revealed.
            </p>
            <div className="flex flex-wrap gap-2">
              {(['json', 'csv', 'pdf'] as const).map((fmt) => (
                <button
                  key={fmt}
                  type="button"
                  className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-slate-100 hover:border-slate-500 disabled:opacity-50"
                  disabled={detail.validationRevealStatus !== 'Revealed' || actionBusy}
                  onClick={async () => {
                    setActionBusy(true);
                    setActionError(null);
                    try {
                      const job = await createExport({
                        scope: 'ValidationExperiment',
                        sourceId: String(detail.id),
                        format: fmt,
                        detailLevel: 'full',
                      });
                      if (job.downloadUrl) {
                        window.open(getExportDownloadUrl(job.exportId), '_blank');
                      }
                    } catch (err) {
                      setActionError(parseApiClientError(err).message);
                    } finally {
                      setActionBusy(false);
                    }
                  }}
                >
                  Export {fmt.toUpperCase()}
                </button>
              ))}
            </div>
          </div>
          <details className="rounded-lg border border-slate-800 px-4 py-3">
            <summary className="cursor-pointer text-slate-200">View Raw Data</summary>
            <div className="mt-3 space-y-3">
              <JsonBlock title="Candidate Reconciliation" value={detail.candidateReconciliationJson} />
              <JsonBlock title="Holdout Exclusivity" value={detail.holdoutExclusivityJson} />
              <JsonBlock title="Metric Consistency" value={detail.metricConsistencyJson} />
              <JsonBlock title="Export Verification" value={detail.exportVerificationJson} />
              <JsonBlock title="Leakage Audit" value={detail.leakageAuditJson} />
              <JsonBlock title="Optimization Objective Snapshot" value={detail.optimizationObjectiveSnapshotJson} />
            </div>
          </details>
        </div>
      ) : null}
    </div>
  );
}

function SelectInline({
  label,
  value,
  onChange,
  options,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  options: string[];
}) {
  return (
    <label className="text-sm text-slate-300">
      <span className="mr-2 text-slate-500">{label}</span>
      <select
        className="rounded-lg border border-slate-700 bg-slate-950 px-2 py-1.5 text-slate-100"
        value={value}
        onChange={(e) => onChange(e.target.value)}
      >
        {options.map((opt) => (
          <option key={opt} value={opt}>
            {opt}
          </option>
        ))}
      </select>
    </label>
  );
}
