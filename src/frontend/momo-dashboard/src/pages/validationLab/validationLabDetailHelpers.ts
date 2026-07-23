import type {
  StrategyRobustnessDecision,
  ValidationCandidateQuery,
  ValidationExperimentStatus,
  ValidationLayerType,
  ValidationRevealStatus,
  ValidationSegmentClassification,
  ValidationSegmentResult,
  ValidationSelectionIntegrityStatus,
} from '@/api/validationLabApi';
import { formatVerdictLabel } from '@/components/common/utils';

export const RESUMABLE_STATUSES = new Set<ValidationExperimentStatus>([
  'Failed',
  'TrainingInterrupted',
  'TrainingPaused',
]);

export const ACTIVE_STATUSES = new Set<ValidationExperimentStatus>([
  'DataPreparing',
  'TrainingRunning',
  'ValidationRunning',
  'ResumePreparing',
  'TrainingResumed',
]);

export function isInsufficientSample(decision?: StrategyRobustnessDecision | null) {
  return (
    decision === 'FailedInsufficientTrainingSample'
    || decision === 'FailedInsufficientValidationSample'
  );
}

/** Beginner-friendly copy for the layers population panel (Milestone 23.0C Part 14). */
export const POPULATION_METRICS_EXPLANATION =
  'Different metrics can use different valid populations. A trade may have valid monetary PnL but be excluded from R-multiple metrics when its risk basis cannot be verified.';

export const POPULATION_COLUMN_LABELS = {
  candidates: 'Candidates',
  pathInputsIncluded: 'Path inputs included',
  pathInputsExcluded: 'Path inputs excluded',
  closedOutcomes: 'Closed outcomes',
  tradesUsedForPnl: 'Trades used for PnL',
  tradesUsedForGrossR: 'Trades used for Gross R',
  tradesUsedForNetR: 'Trades used for Net R',
  includedWithWarnings: 'Included trades with warnings',
} as const;

/**
 * Displays a population count, preferring v1.3.2 fields and falling back to legacy aliases.
 * Missing optional fields remain backward-compatible (show "—").
 */
export function formatPopulationCount(
  preferred?: number | null,
  legacyFallback?: number | null,
): string {
  if (preferred != null) return String(preferred);
  if (legacyFallback != null) return String(legacyFallback);
  return '—';
}

/**
 * Net expectancy display: zero Net R population must show NotEvaluated, not a fabricated 0.
 * Historical rows without population fields keep null → "—" / numeric value behavior.
 */
export function formatNetExpectancyDisplay(segment: Pick<
  ValidationSegmentResult,
  'netExpectancyR' | 'netRPopulationCount' | 'netExpectancyApplicability' | 'populationContractVersion'
>): string {
  if (
    segment.netExpectancyApplicability === 'NotEvaluated'
    || (segment.netRPopulationCount != null && segment.netRPopulationCount === 0)
  ) {
    return 'NotEvaluated';
  }
  if (segment.netExpectancyR == null) return '—';
  return String(segment.netExpectancyR);
}

/** True when candidate population must not be mistaken for the Net R population. */
export function candidateCountDistinctFromNetR(segment: ValidationSegmentResult): boolean {
  const candidates = segment.candidatePopulationCount ?? segment.candidateCount;
  const netR = segment.netRPopulationCount;
  if (netR == null) return true;
  return candidates !== netR;
}

export function exclusionReasonsDistinctFromWarnings(segment: ValidationSegmentResult): boolean {
  const warnings = new Set(segment.metricWarningCodes ?? []);
  // Exclusion reasons live in metrics JSON / exclusion counts; UI must not treat warning codes as exclusions.
  return !warnings.has('MissingExitPrice') && !warnings.has('MissingPathQuantity');
}

export function verdictTone(decision?: StrategyRobustnessDecision | null): 'success' | 'warning' | 'info' | 'neutral' {
  if (!decision) return 'neutral';
  if (decision === 'Passed' || decision === 'ConditionallyPassed') return 'success';
  if (isInsufficientSample(decision)) return 'warning';
  if (decision.startsWith('Failed') || decision === 'Invalid') return 'warning';
  return 'info';
}

/** Shares wording with the generic verdict label formatter used elsewhere in the dashboard. */
export const formatExperimentVerdict = formatVerdictLabel;

export function tryParseJson(raw?: string | null): unknown {
  if (!raw) return null;
  try {
    return JSON.parse(raw);
  } catch {
    return raw;
  }
}

export function isLegacyMetrics(version?: string | null) {
  return (
    version === 'ValidationMetrics/v1'
    || version === 'ValidationMetrics/v1.2'
  );
}

export function selectionIntegrityAllowsFreeze(status?: string | null) {
  return status === 'Passed' || status === 'Valid' || status === 'InfrastructureOnlyFallback';
}

export function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

export function pickLayer(
  segments: ValidationSegmentResult[] | null | undefined,
  segmentType: 'Training' | 'Validation',
  layer: ValidationLayerType = 'RawStrategy',
) {
  return (segments ?? []).find((s) => s.segmentType === segmentType && s.layerType === layer);
}

export interface ExperimentActionAvailability {
  canPrepare: boolean;
  canTrain: boolean;
  canResumeTraining: boolean;
  canFreeze: boolean;
  canValidate: boolean;
  canCloneOrRerun: boolean;
  zeroEligibleFailure: boolean;
}

export interface ExperimentActionAvailabilityInput {
  status: ValidationExperimentStatus;
  selectionIntegrityStatus?: ValidationSelectionIntegrityStatus | null;
  strategyRobustnessDecision?: StrategyRobustnessDecision | null;
  validationRevealStatus: ValidationRevealStatus;
}

/**
 * Pure derivation of which experiment actions the UI should offer.
 * Centralizing this keeps the "zero eligible trials disables freeze" and
 * "invalid selection integrity disables validation" rules testable without
 * mounting the full detail page.
 */
export function computeExperimentActionAvailability(
  detail: ExperimentActionAvailabilityInput,
): ExperimentActionAvailability {
  const canPrepare = detail.status === 'Draft' || detail.status === 'DataPreparing' || detail.status === 'Failed';
  const canTrain = detail.status === 'DataReady';
  const canResumeTraining = RESUMABLE_STATUSES.has(detail.status);
  const canFreeze =
    detail.status === 'TrainingCompleted'
    && selectionIntegrityAllowsFreeze(detail.selectionIntegrityStatus);
  const canValidate =
    detail.status === 'ConfigurationFrozen'
    && selectionIntegrityAllowsFreeze(detail.selectionIntegrityStatus);
  const zeroEligibleFailure =
    detail.strategyRobustnessDecision === 'FailedNoTrainingTrialPassedGuardrails'
    || detail.selectionIntegrityStatus === 'FailedNoEligibleTrials';
  const canCloneOrRerun =
    detail.status === 'Completed'
    || detail.status === 'Failed'
    || detail.status === 'Cancelled'
    || detail.status === 'ConfigurationFrozen'
    || detail.validationRevealStatus === 'Revealed';

  return { canPrepare, canTrain, canResumeTraining, canFreeze, canValidate, canCloneOrRerun, zeroEligibleFailure };
}

/**
 * Builds the candidates-tab query, or returns null when the request should
 * not be issued at all (Validation segment candidates stay hidden until reveal).
 */
export function buildValidationCandidateQuery(
  segment: ValidationSegmentClassification | 'CrossSegmentOverlap',
  layer: ValidationLayerType,
  revealed: boolean,
  page = 1,
  pageSize = 50,
): ValidationCandidateQuery | null {
  if (segment === 'Validation' && !revealed) {
    return null;
  }
  const overlapOnly = segment === 'CrossSegmentOverlap';
  return {
    segment: overlapOnly ? undefined : segment,
    layer,
    crossSegmentOverlapOnly: overlapOnly ? true : undefined,
    metricClassification: overlapOnly ? 'CrossSegmentOverlapExcludedFromValidation' : undefined,
    page,
    pageSize,
  };
}
