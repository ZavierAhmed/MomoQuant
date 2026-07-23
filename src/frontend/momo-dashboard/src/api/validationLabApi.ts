import { apiRequest } from '@/api/apiClient';
import type { StrategyLabObservationSettings } from '@/api/strategyLabApi';

export type ValidationExperimentType =
  | 'ValidateExistingFrozenConfiguration'
  | 'TrainingSearchHoldoutValidation';

export type ValidationExperimentStatus =
  | 'Draft'
  | 'DataPreparing'
  | 'DataReady'
  | 'TrainingRunning'
  | 'TrainingCompleted'
  | 'ConfigurationFrozen'
  | 'ValidationRunning'
  | 'Completed'
  | 'Failed'
  | 'Cancelled'
  | 'TrainingInterrupted'
  | 'ResumePreparing'
  | 'TrainingResumed'
  | 'TrainingPaused';

export type ValidationRevealStatus = 'Hidden' | 'Frozen' | 'Revealed';

export type ValidationPrimaryQualificationLayer =
  | 'RawStrategy'
  | 'ConfidenceQualified'
  | 'RiskOnly'
  | 'FullPipeline';

export type ValidationSegmentType = 'Training' | 'Validation';

export type ValidationLayerType =
  | 'RawStrategy'
  | 'ConfidenceQualified'
  | 'RiskOnly'
  | 'FullPipeline';

export type ValidationSegmentClassification =
  | 'Training'
  | 'Validation'
  | 'BoundaryCensored'
  | 'WarmupSuppressed'
  | 'Invalid'
  | 'ExcludedBySegmentSessionReset'
  | 'AddedBySegmentSessionReset';

export type CandidateReconciliationStatus =
  | 'ExactMatch'
  | 'ExactMatchWithBoundaryCensoring'
  | 'ExplainedSessionBoundaryDifference'
  | 'UnexplainedDifference'
  | 'Invalid';

export type ParameterStabilityApplicability =
  | 'Applicable'
  | 'NotApplicable'
  | 'InsufficientTrials'
  | 'Evaluated';

export type ValidationLeakageAuditStatus = 'Passed' | 'Failed' | 'NotAvailable';

export type ValidationExportVerificationStatus = 'Passed' | 'Failed' | 'NotRun';

export type ValidationLaboratoryReadiness = 'Ready' | 'ReadyWithWarnings' | 'Blocked';

export type ValidationExperimentSupersessionStatus = 'None' | 'Superseded';

export type ValidationSelectionIntegrityStatus =
  | 'NotEvaluated'
  | 'Valid'
  | 'Passed'
  | 'InvalidSelectedTrial'
  | 'SelectionPolicyViolation'
  | 'NoEligibleTrial'
  | 'FailedNoEligibleTrials'
  | 'FailedSelectedTrialMissing'
  | 'FailedSelectedTrialNotTerminal'
  | 'FailedSelectedTrialIneligible'
  | 'FailedSelectedTrialNotRanked'
  | 'FailedParameterFingerprintMismatch'
  | 'FailedFrozenSnapshotMissing'
  | 'FailedFrozenSnapshotEmpty'
  | 'FailedFrozenFingerprintInvalid'
  | 'FailedMultipleSelectedTrials'
  | 'InfrastructureOnlyFallback';

export type StrategyRobustnessDecision =
  | 'Passed'
  | 'ConditionallyPassed'
  | 'FailedInsufficientTrainingSample'
  | 'FailedInsufficientValidationSample'
  | 'FailedNegativeTrainingExpectancy'
  | 'FailedNegativeValidationExpectancy'
  | 'FailedNoTrainingTrialPassedGuardrails'
  | 'FailedPerformanceCollapse'
  | 'FailedExcessiveValidationDrawdown'
  | 'FailedOpportunityCollapse'
  | 'FailedParameterInstability'
  | 'FailedCostSensitivity'
  | 'FailedDataQuality'
  | 'FailedConfigurationMismatch'
  | 'Invalid'
  | 'FailedDataIntegrity';

export type ValidationTrialStatus =
  | 'Pending'
  | 'Running'
  | 'Completed'
  | 'Failed'
  | 'GuardrailRejected'
  | 'Interrupted';

export interface ValidationTrainingProgress {
  requestedTrialCount: number;
  generatedTrialCount: number;
  pendingTrialCount: number;
  runningTrialCount: number;
  completedTrialCount: number;
  failedTrialCount: number;
  interruptedTrialCount: number;
  skippedCompletedTrialCount: number;
  progressPercent: number;
  lastProgressAtUtc?: string | null;
  activeTrialNumber?: number | null;
  activeStrategyLabRunId?: number | null;
}

export interface ValidationTrialRecoveryReport {
  recoveredTrialNumbers: number[];
  unrecoverableTrialNumbers: number[];
  skippedAlreadyPersisted: number[];
  summary: string;
}

export interface ValidationQualificationProfile {
  profileVersion?: string;
  primaryQualificationLayer?: ValidationPrimaryQualificationLayer;
  minimumTrainingClosedTrades?: number;
  minimumValidationClosedTrades?: number;
  minimumTrainingProfitFactor?: number;
  minimumValidationProfitFactor?: number;
  minimumTrainingNetExpectancyR?: number;
  minimumValidationNetExpectancyR?: number;
  maximumTrainingDrawdownPercent?: number;
  maximumValidationDrawdownPercent?: number;
  minimumOpportunityRetentionPercent?: number;
  maximumAllowedExpectancyDegradation?: number;
  maximumSingleTradePnlContributionPercent?: number;
  requirePositiveValidationNetPnl?: boolean;
  requirePositiveValidationNetExpectancy?: boolean;
  requireParameterStability?: boolean;
}

export interface CreateValidationExperimentRequest {
  name?: string;
  description?: string;
  experimentType?: ValidationExperimentType;
  strategyCode: string;
  strategyVersion?: string;
  sourceStrategyLabRunId?: number | null;
  exchangeId: number;
  symbolId: number;
  timeframe: string;
  requestedStartUtc: string;
  requestedEndUtc: string;
  splitRatio?: number;
  requiredWarmupCandles?: number;
  strategyParameters?: Record<string, string>;
  parameterSearchSpaceOverrides?: Record<string, string>;
  observationSettings?: StrategyLabObservationSettings;
  qualificationProfile?: ValidationQualificationProfile;
  primaryQualificationLayer?: ValidationPrimaryQualificationLayer;
  initialBalance?: number;
  makerFeeRate?: number;
  takerFeeRate?: number;
  slippagePercent?: number;
  maximumTrials?: number;
  deterministicSeed?: number;
  autoImportMissingCandles?: boolean;
}

export interface UpdateValidationExperimentRequest {
  name?: string;
  description?: string;
  requestedStartUtc?: string;
  requestedEndUtc?: string;
  splitRatio?: number;
  requiredWarmupCandles?: number;
  strategyParameters?: Record<string, string>;
  parameterSearchSpaceOverrides?: Record<string, string>;
  observationSettings?: StrategyLabObservationSettings;
  qualificationProfile?: ValidationQualificationProfile;
  primaryQualificationLayer?: ValidationPrimaryQualificationLayer;
  initialBalance?: number;
  makerFeeRate?: number;
  takerFeeRate?: number;
  slippagePercent?: number;
  maximumTrials?: number;
  deterministicSeed?: number;
}

export interface ValidationExperiment {
  id: number;
  name: string;
  description?: string | null;
  experimentType: ValidationExperimentType;
  status: ValidationExperimentStatus;
  strategyCode: string;
  strategyVersion: string;
  sourceStrategyLabRunId?: number | null;
  exchangeId: number;
  exchange: string;
  symbolId: number;
  symbol: string;
  timeframe: string;
  requestedStartUtc: string;
  requestedEndUtc: string;
  splitRatio: number;
  splitAlgorithmVersion: string;
  totalEligibleCandleCount: number;
  trainingCandleCount: number;
  validationCandleCount: number;
  trainingStartUtc?: string | null;
  trainingEndUtc?: string | null;
  validationStartUtc?: string | null;
  validationEndUtc?: string | null;
  splitCandleOpenTimeUtc?: string | null;
  requiredWarmupCandles: number;
  trainingWarmupStartUtc?: string | null;
  validationWarmupStartUtc?: string | null;
  candleDataFingerprint: string;
  validationRevealStatus: ValidationRevealStatus;
  primaryQualificationLayer: ValidationPrimaryQualificationLayer;
  primaryLayerWarning?: string | null;
  frozenAtUtc?: string | null;
  validationRevealedAtUtc?: string | null;
  strategyRobustnessDecision?: StrategyRobustnessDecision | null;
  primaryFailureReason?: string | null;
  decisionExplanation?: string | null;
  boundaryCensoredCount: number;
  initialBalance: number;
  maximumTrials: number;
  deterministicSeed: number;
  errorMessage?: string | null;
  currentStage?: string | null;
  percentComplete: number;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
  trainingStrategyLabRunId?: number | null;
  validationStrategyLabRunId?: number | null;
  frozenParameterFingerprint?: string | null;
  validationMetricsVersion?: string;
  candidateReconciliationStatus?: CandidateReconciliationStatus | null;
  leakageAuditStatus?: ValidationLeakageAuditStatus | null;
  parameterStabilityApplicability?: ParameterStabilityApplicability | null;
  segmentDetectorContinuityMode?: string;
  expectancyMetric?: string;
  profitFactorMetric?: string;
  holdoutExclusivityPolicyVersion?: string;
  crossSegmentOverlapCount?: number;
  metricConsistencyStatus?: string;
  exportVerificationStatus?: ValidationExportVerificationStatus | null;
  validationLaboratoryReadinessStatus?: ValidationLaboratoryReadiness | null;
  isCanonical?: boolean;
  supersessionStatus?: ValidationExperimentSupersessionStatus;
  supersededByExperimentId?: number | null;
  supersededAtUtc?: string | null;
  supersessionReason?: string | null;
  selectionIntegrityStatus?: ValidationSelectionIntegrityStatus;
  selectedTrialId?: number | null;
  selectedTrialNumber?: number | null;
  selectedTrialParameterFingerprint?: string | null;
  frozenSnapshotValidationStatus?: string;
  selectionIntegrityVersion?: string;
  riskBasisVersion?: string;
  parameterFingerprintVersion?: string;
  freezeSource?: string | null;
  isQualificationCapable?: boolean;
  trialPopulationSummaryJson?: string | null;
  closeoutAuditJson?: string | null;
}

export interface ValidationSegmentResult {
  id: number;
  segmentType: ValidationSegmentType;
  layerType: ValidationLayerType;
  strategyLabRunId?: number | null;
  metricsJson: string;
  candleCount: number;
  candidateCount: number;
  closedTradeCount: number;
  netExpectancyR?: number | null;
  profitFactor?: number | null;
  netPnl?: number | null;
  netReturnPercent?: number | null;
  maximumDrawdownPercent?: number | null;
  transactionCosts?: number | null;
  boundaryCensoredCount: number;
  resultFingerprint: string;
  resultCalculationVersion?: string;
  grossExpectancyR?: number | null;
  grossProfitFactor?: number | null;
  netProfitFactor?: number | null;
  grossAverageR?: number | null;
  netAverageR?: number | null;
  grossPnl?: number | null;
  persistedCandidateRowCount?: number;
  metricIncludedCandidateCount?: number;
  metricExcludedCandidateCount?: number;
  crossSegmentOverlapCount?: number;
  grossProfit?: number | null;
  grossLoss?: number | null;
  netProfit?: number | null;
  netLoss?: number | null;
}

export interface HoldoutReuseWarning {
  priorExperimentIds: number[];
  overlapPercent: number;
  revealCount: number;
  firstRevealedAtUtc?: string | null;
  contaminationRisk: string;
  repeatedHoldoutExposure: boolean;
}

export interface ValidationExperimentDetail extends ValidationExperiment {
  candleDataSnapshotJson: string;
  warmupSnapshotJson: string;
  parameterSearchSpaceSnapshotJson: string;
  optimizationObjectiveSnapshotJson: string;
  frozenStrategyParameterSnapshotJson?: string | null;
  frozenStrategyFingerprint?: string | null;
  frozenConfidenceSnapshotJson?: string | null;
  frozenRiskSnapshotJson?: string | null;
  frozenCostModelSnapshotJson?: string | null;
  qualificationProfileSnapshotJson: string;
  failureReasonsJson?: string | null;
  qualificationRuleResultsJson?: string | null;
  diagnosticsJson: string;
  overlayResultsJson?: string | null;
  comparisonJson?: string | null;
  regimeComparisonJson?: string | null;
  parameterStabilityJson?: string | null;
  candidateReconciliationJson?: string | null;
  leakageAuditJson?: string | null;
  holdoutExclusivityJson?: string | null;
  metricConsistencyJson?: string | null;
  exportVerificationJson?: string | null;
  draftConfigurationJson: string;
  segmentResults?: ValidationSegmentResult[] | null;
  holdoutReuseWarning?: HoldoutReuseWarning | null;
}

export interface ValidationParameterTrial {
  id: number;
  trialNumber: number;
  parameterSnapshotJson: string;
  parameterFingerprint: string;
  status: ValidationTrialStatus;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  rawCandidateCount: number;
  closedTradeCount: number;
  winnerCount: number;
  loserCount: number;
  expiredCount: number;
  netExpectancyR?: number | null;
  grossPnl?: number | null;
  netPnl?: number | null;
  profitFactor?: number | null;
  maximumDrawdownPercent?: number | null;
  feeImpactPercent?: number | null;
  trainingScore?: number | null;
  guardrailDecision: string;
  guardrailFailureReasonsJson?: string | null;
  rank?: number | null;
  strategyLabRunId?: number | null;
  errorMessage?: string | null;
  recoverySource?: string | null;
}

export interface ValidationCandidateQuery {
  segment?: ValidationSegmentClassification;
  layer?: ValidationLayerType;
  metricClassification?: string;
  crossSegmentOverlapOnly?: boolean;
  page?: number;
  pageSize?: number;
}

export interface ValidationLaboratoryReadinessCheck {
  key: string;
  message: string;
  passed: boolean;
  isWarning?: boolean;
}

export interface ValidationExperimentReadinessItem {
  experimentId: number;
  name: string;
  status: ValidationLaboratoryReadiness;
  metricsVersion?: string | null;
  notes?: string | null;
}

export interface ValidationLaboratoryReadinessReport {
  status: ValidationLaboratoryReadiness;
  checks: ValidationLaboratoryReadinessCheck[];
  experiments: ValidationExperimentReadinessItem[];
  summary: string;
}

export interface PagedValidationCandidates {
  items: Record<string, unknown>[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

export const validationLabApi = {
  getReadiness: () =>
    apiRequest<ValidationLaboratoryReadinessReport>('/validation-lab/readiness'),

  runMilestone223Closeout: () =>
    apiRequest<Record<string, unknown>>('/validation-lab/closeout/milestone-223', { method: 'POST' }),

  auditExperimentCloseout: (id: number, verifyExports = true) =>
    apiRequest<Record<string, unknown>>(
      `/validation-lab/experiments/${id}/closeout-audit?verifyExports=${verifyExports}`,
      { method: 'POST' },
    ),

  createExperiment: (request: CreateValidationExperimentRequest) =>
    apiRequest<ValidationExperiment>('/validation-lab/experiments', { method: 'POST', body: request }),

  getExperiments: (limit = 50) =>
    apiRequest<ValidationExperiment[]>(`/validation-lab/experiments?limit=${limit}`),

  getExperiment: (id: number) =>
    apiRequest<ValidationExperimentDetail>(`/validation-lab/experiments/${id}`),

  updateExperiment: (id: number, request: UpdateValidationExperimentRequest) =>
    apiRequest<ValidationExperiment>(`/validation-lab/experiments/${id}`, { method: 'PUT', body: request }),

  prepareData: (id: number) =>
    apiRequest<ValidationExperiment>(`/validation-lab/experiments/${id}/prepare-data`, { method: 'POST' }),

  runTraining: (id: number) =>
    apiRequest<ValidationExperiment>(`/validation-lab/experiments/${id}/run-training`, { method: 'POST' }),

  resumeTraining: (id: number) =>
    apiRequest<ValidationExperiment>(`/validation-lab/experiments/${id}/resume-training`, { method: 'POST' }),

  recoverTrials: (id: number) =>
    apiRequest<ValidationTrialRecoveryReport>(`/validation-lab/experiments/${id}/recover-trials`, { method: 'POST' }),

  getTrainingProgress: (id: number) =>
    apiRequest<ValidationTrainingProgress>(`/validation-lab/experiments/${id}/training-progress`),

  getTrainingTrials: (id: number) =>
    apiRequest<ValidationParameterTrial[]>(`/validation-lab/experiments/${id}/training-trials`),

  freeze: (id: number) =>
    apiRequest<ValidationExperiment>(`/validation-lab/experiments/${id}/freeze`, { method: 'POST' }),

  runValidation: (id: number) =>
    apiRequest<ValidationExperimentDetail>(`/validation-lab/experiments/${id}/run-validation`, { method: 'POST' }),

  getComparison: (id: number) =>
    apiRequest<Record<string, unknown>>(`/validation-lab/experiments/${id}/comparison`),

  getConfidenceAnalysis: (id: number) =>
    apiRequest<Record<string, unknown>>(`/validation-lab/experiments/${id}/confidence-analysis`),

  getRiskAnalysis: (id: number) =>
    apiRequest<Record<string, unknown>>(`/validation-lab/experiments/${id}/risk-analysis`),

  getCandidates: (id: number, query: ValidationCandidateQuery = {}, signal?: AbortSignal) => {
    const params = new URLSearchParams();
    Object.entries(query).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        params.set(key, String(value));
      }
    });
    const qs = params.toString();
    return apiRequest<PagedValidationCandidates>(
      `/validation-lab/experiments/${id}/candidates${qs ? `?${qs}` : ''}`,
      { signal },
    );
  },

  getDiagnostics: (id: number) =>
    apiRequest<Record<string, unknown>>(`/validation-lab/experiments/${id}/diagnostics`),

  getReconciliation: (id: number) =>
    apiRequest<Record<string, unknown>>(`/validation-lab/experiments/${id}/reconciliation`),

  getLeakageAudit: (id: number) =>
    apiRequest<Record<string, unknown>>(`/validation-lab/experiments/${id}/leakage-audit`),

  getExclusivity: (id: number) =>
    apiRequest<Record<string, unknown>>(`/validation-lab/experiments/${id}/exclusivity`),

  getSelectionIntegrity: (id: number) =>
    apiRequest<Record<string, unknown>>(`/validation-lab/experiments/${id}/selection-integrity`),

  getMetricBasisAudit: (id: number) =>
    apiRequest<Record<string, unknown>>(`/validation-lab/experiments/${id}/metric-basis-audit`),

  recalculateMetrics: (
    id: number,
    body: {
      targetMetricsVersion?: string;
      targetRiskBasisVersion?: string;
      reason: string;
      preserveOriginal?: boolean;
    },
  ) =>
    apiRequest<Record<string, unknown>>(`/validation-lab/experiments/${id}/recalculate-metrics`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  recalculateVerdict: (id: number) =>
    apiRequest<ValidationExperimentDetail>(`/validation-lab/experiments/${id}/recalculate-verdict`, {
      method: 'POST',
    }),

  clone: (id: number) =>
    apiRequest<ValidationExperiment>(`/validation-lab/experiments/${id}/clone`, { method: 'POST' }),

  rerunExactly: (id: number) =>
    apiRequest<ValidationExperiment>(`/validation-lab/experiments/${id}/rerun-exactly`, { method: 'POST' }),
};
