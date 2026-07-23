import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import type { ValidationSegmentResult } from '@/api/validationLabApi'
import { PopulationMetricsLegend } from '@/pages/validationLab/PopulationMetricsLegend'
import {
  POPULATION_COLUMN_LABELS,
  POPULATION_METRICS_EXPLANATION,
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
