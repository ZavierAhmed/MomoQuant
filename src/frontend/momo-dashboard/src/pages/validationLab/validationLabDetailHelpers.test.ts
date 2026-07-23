import { describe, expect, it } from 'vitest'
import {
  buildValidationCandidateQuery,
  computeExperimentActionAvailability,
  isInsufficientSample,
  selectionIntegrityAllowsFreeze,
  verdictTone,
} from '@/pages/validationLab/validationLabDetailHelpers'

describe('computeExperimentActionAvailability', () => {
  it('disables Freeze when zero training trials passed guardrails (zero eligible trials)', () => {
    const availability = computeExperimentActionAvailability({
      status: 'TrainingCompleted',
      selectionIntegrityStatus: 'FailedNoEligibleTrials',
      strategyRobustnessDecision: 'FailedNoTrainingTrialPassedGuardrails',
      validationRevealStatus: 'Hidden',
    })

    expect(availability.canFreeze).toBe(false)
    expect(availability.zeroEligibleFailure).toBe(true)
  })

  it('disables Run Validation when selection integrity is invalid', () => {
    const availability = computeExperimentActionAvailability({
      status: 'ConfigurationFrozen',
      selectionIntegrityStatus: 'InvalidSelectedTrial',
      strategyRobustnessDecision: null,
      validationRevealStatus: 'Frozen',
    })

    expect(availability.canValidate).toBe(false)
  })

  it('enables Freeze once selection integrity passes and training completed', () => {
    const availability = computeExperimentActionAvailability({
      status: 'TrainingCompleted',
      selectionIntegrityStatus: 'Passed',
      strategyRobustnessDecision: null,
      validationRevealStatus: 'Hidden',
    })

    expect(availability.canFreeze).toBe(true)
    expect(availability.zeroEligibleFailure).toBe(false)
  })

  it('enables Run Validation once configuration is frozen with valid selection integrity', () => {
    const availability = computeExperimentActionAvailability({
      status: 'ConfigurationFrozen',
      selectionIntegrityStatus: 'Valid',
      strategyRobustnessDecision: null,
      validationRevealStatus: 'Frozen',
    })

    expect(availability.canValidate).toBe(true)
  })

  it('allows the infrastructure-only fallback selection status to freeze', () => {
    expect(selectionIntegrityAllowsFreeze('InfrastructureOnlyFallback')).toBe(true)
    expect(selectionIntegrityAllowsFreeze('FailedSelectedTrialMissing')).toBe(false)
  })
})

describe('isInsufficientSample / verdictTone', () => {
  it('flags insufficient sample decisions', () => {
    expect(isInsufficientSample('FailedInsufficientTrainingSample')).toBe(true)
    expect(isInsufficientSample('FailedInsufficientValidationSample')).toBe(true)
    expect(isInsufficientSample('Passed')).toBe(false)
  })

  it('tones warnings for insufficient sample and generic failures', () => {
    expect(verdictTone('FailedInsufficientTrainingSample')).toBe('warning')
    expect(verdictTone('Passed')).toBe('success')
    expect(verdictTone(null)).toBe('neutral')
  })
})

describe('buildValidationCandidateQuery', () => {
  it('returns null for Validation segment before reveal (no request should be issued)', () => {
    expect(buildValidationCandidateQuery('Validation', 'RawStrategy', false)).toBeNull()
  })

  it('sends the correct query once revealed', () => {
    expect(buildValidationCandidateQuery('Validation', 'RawStrategy', true)).toEqual({
      segment: 'Validation',
      layer: 'RawStrategy',
      crossSegmentOverlapOnly: undefined,
      metricClassification: undefined,
      page: 1,
      pageSize: 50,
    })
  })

  it('maps CrossSegmentOverlap pseudo-segment to overlap-only query params', () => {
    expect(buildValidationCandidateQuery('CrossSegmentOverlap', 'FullPipeline', true, 2, 25)).toEqual({
      segment: undefined,
      layer: 'FullPipeline',
      crossSegmentOverlapOnly: true,
      metricClassification: 'CrossSegmentOverlapExcludedFromValidation',
      page: 2,
      pageSize: 25,
    })
  })

  it('allows Training segment candidates regardless of reveal status', () => {
    expect(buildValidationCandidateQuery('Training', 'RawStrategy', false)).toEqual({
      segment: 'Training',
      layer: 'RawStrategy',
      crossSegmentOverlapOnly: undefined,
      metricClassification: undefined,
      page: 1,
      pageSize: 50,
    })
  })
})
