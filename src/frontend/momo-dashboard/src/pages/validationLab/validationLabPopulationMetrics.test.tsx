import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import type { ValidationSegmentResult } from '@/api/validationLabApi'
import { PopulationMetricsLegend } from '@/pages/validationLab/PopulationMetricsLegend'
import {
  POPULATION_COLUMN_LABELS,
  POPULATION_METRICS_EXPLANATION,
  COMPLETE_PATH_INTEGRITY_LABEL,
  formatRankEligibility,
  getWarmupDisplay,
  GUARDRAIL_NOT_EVALUATED_EXPLANATION,
  INCLUDED_POPULATION_RISK_LABEL,
  candidateCountDistinctFromNetR,
  exclusionReasonsDistinctFromWarnings,
  formatNetExpectancyDisplay,
  formatPopulationCount,
} from '@/pages/validationLab/validationLabDetailHelpers'

function segment(partial: Partial<ValidationSegmentResult>): ValidationSegmentResult {
  return {
    id: 1,
    segmentType: 'Training',
    layerType: 'RawStrategy',
    metricsJson: '{}',
    candleCount: 100,
    candidateCount: 10,
    closedTradeCount: 5,
    boundaryCensoredCount: 0,
    resultFingerprint: 'fp',
    ...partial,
  }
}

describe('PopulationMetricsLegend', () => {
  it('renders separate population labels and explanation', () => {
    render(<PopulationMetricsLegend />)
    expect(screen.getByText(POPULATION_METRICS_EXPLANATION)).toBeInTheDocument()
    expect(screen.getByText(POPULATION_COLUMN_LABELS.candidates)).toBeInTheDocument()
    expect(screen.getByText(POPULATION_COLUMN_LABELS.pathInputsIncluded)).toBeInTheDocument()
    expect(screen.getByText(POPULATION_COLUMN_LABELS.pathInputsExcluded)).toBeInTheDocument()
    expect(screen.getByText(POPULATION_COLUMN_LABELS.closedOutcomes)).toBeInTheDocument()
    expect(screen.getByText(POPULATION_COLUMN_LABELS.tradesUsedForPnl)).toBeInTheDocument()
    expect(screen.getByText(POPULATION_COLUMN_LABELS.tradesUsedForGrossR)).toBeInTheDocument()
    expect(screen.getByText(POPULATION_COLUMN_LABELS.tradesUsedForNetR)).toBeInTheDocument()
    expect(screen.getByText(POPULATION_COLUMN_LABELS.includedWithWarnings)).toBeInTheDocument()
  })
})

describe('population display helpers', () => {
  it('keeps candidate count distinct from Net R population', () => {
    const row = segment({
      candidatePopulationCount: 7,
      netRPopulationCount: 3,
      candidateCount: 7,
    })
    expect(candidateCountDistinctFromNetR(row)).toBe(true)
    expect(formatPopulationCount(row.candidatePopulationCount, row.candidateCount)).toBe('7')
    expect(formatPopulationCount(row.netRPopulationCount)).toBe('3')
  })

  it('treats exclusion reasons as distinct from warning codes', () => {
    const warned = segment({
      metricWarningCodes: ['CandidateRawPnlReconciliationMismatch'],
      metricWarningBearingIncludedTradeCount: 1,
      excludedPathInputCount: 1,
    })
    expect(exclusionReasonsDistinctFromWarnings(warned)).toBe(true)
    expect(warned.metricWarningCodes).not.toContain('MissingPathQuantity')
  })

  it('shows NotEvaluated for zero Net R population instead of zero expectancy', () => {
    expect(
      formatNetExpectancyDisplay(
        segment({
          netExpectancyR: 0,
          netRPopulationCount: 0,
          netExpectancyApplicability: 'NotEvaluated',
        }),
      ),
    ).toBe('NotEvaluated')
  })

  it('keeps historical v1.3 readable when population fields are absent', () => {
    const legacy = segment({
      netExpectancyR: 0.5,
      resultCalculationVersion: 'ValidationMetrics/v1.3',
      populationContractVersion: null,
      netRPopulationCount: null,
      candidatePopulationCount: null,
    })
    expect(formatNetExpectancyDisplay(legacy)).toBe('0.5')
    expect(formatPopulationCount(legacy.candidatePopulationCount, legacy.candidateCount)).toBe('10')
    expect(formatPopulationCount(legacy.monetaryPnlPopulationCount)).toBe('—')
  })

  it('renders v1.3.2 population fields when present', () => {
    const v132 = segment({
      populationContractVersion: 'ValidationMetricPopulation/v1',
      candidatePopulationCount: 7,
      includedPathInputCount: 6,
      excludedPathInputCount: 1,
      monetaryPnlPopulationCount: 5,
      grossRPopulationCount: 3,
      netRPopulationCount: 3,
      netExpectancyR: 0.4,
    })
    expect(formatPopulationCount(v132.candidatePopulationCount)).toBe('7')
    expect(formatPopulationCount(v132.includedPathInputCount)).toBe('6')
    expect(formatPopulationCount(v132.excludedPathInputCount)).toBe('1')
    expect(formatPopulationCount(v132.monetaryPnlPopulationCount)).toBe('5')
    expect(formatPopulationCount(v132.grossRPopulationCount)).toBe('3')
    expect(formatPopulationCount(v132.netRPopulationCount)).toBe('3')
    expect(formatNetExpectancyDisplay(v132)).toBe('0.4')
  })

  it('remains backward compatible when optional population fields are missing from the API contract', () => {
    const sparse = segment({})
    expect(formatPopulationCount(sparse.includedPathInputCount, sparse.metricIncludedCandidateCount)).toBe('—')
    expect(formatPopulationCount(sparse.netRPopulationCount)).toBe('—')
    expect(formatNetExpectancyDisplay(sparse)).toBe('—')
  })
})

describe('Milestone 23.0D detail display', () => {
  it('shows warm-up required, available, and complete status from new DTO fields', () => {
    expect(getWarmupDisplay({
      requiredWarmupCandles: 100,
      availableWarmupCandles: 100,
      warmupStatus: 'Complete',
    })).toEqual({ required: 100, available: 100, status: 'Complete' })
  })

  it('falls back to the historical warm-up snapshot shape', () => {
    expect(getWarmupDisplay({
      requiredWarmupCandles: 20,
      warmupSnapshotJson: JSON.stringify({
        requiredWarmupCandleCount: 30,
        availableWarmupCandleCount: 12,
        warmupStatus: 'Insufficient',
      }),
    })).toEqual({ required: 30, available: 12, status: 'Insufficient' })
  })

  it('keeps missing optional warm-up fields backward compatible', () => {
    expect(getWarmupDisplay({ requiredWarmupCandles: 20 })).toEqual({
      required: 20,
      available: undefined,
      status: undefined,
    })
  })

  it('uses distinct beginner labels for included risk and complete path integrity', () => {
    expect(INCLUDED_POPULATION_RISK_LABEL).toContain('trades used by the metrics')
    expect(COMPLETE_PATH_INTEGRITY_LABEL).toContain('including excluded')
    expect(INCLUDED_POPULATION_RISK_LABEL).not.toBe(COMPLETE_PATH_INTEGRITY_LABEL)
  })

  it('explains that NotEvaluated is not numeric zero', () => {
    expect(GUARDRAIL_NOT_EVALUATED_EXPLANATION).toContain('not the numeric value 0')
    expect(GUARDRAIL_NOT_EVALUATED_EXPLANATION).toContain('rank-ineligible')
  })

  it('renders an ineligible trial with persisted reasons', () => {
    expect(formatRankEligibility(
      'Ineligible',
      '["GUARDRAIL_NET_EXPECTANCY_NOT_EVALUATED"]',
    )).toBe('Ineligible — GUARDRAIL_NET_EXPECTANCY_NOT_EVALUATED')
  })

  it('renders eligible and historical missing eligibility without inventing a status', () => {
    expect(formatRankEligibility('Eligible')).toBe('Eligible')
    expect(formatRankEligibility(undefined)).toBe('—')
  })
})
