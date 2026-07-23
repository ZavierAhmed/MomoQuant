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
