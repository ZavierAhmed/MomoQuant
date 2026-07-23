import { describe, expect, it } from 'vitest'
import { buildCandidateQuery, type CandidateGridFilterState } from '@/components/strategies/candidateGrid/strategyLabCandidateGridHelpers'

function baseState(overrides: Partial<CandidateGridFilterState> = {}): CandidateGridFilterState {
  return {
    page: 1,
    pageSize: 50,
    sortBy: 'setupDetectedAtUtc',
    sortDirection: 'desc',
    search: '',
    direction: '',
    rawOutcome: '',
    confidenceDecision: '',
    riskDecision: '',
    confidenceMin: '',
    confidenceMax: '',
    riskMin: '',
    riskMax: '',
    profitableOnly: '',
    fromUtc: '',
    toUtc: '',
    quickFilter: '',
    ...overrides,
  }
}

describe('buildCandidateQuery', () => {
  it('sends the correct pagination and sort params for the default state', () => {
    expect(buildCandidateQuery(baseState())).toEqual({
      page: 1,
      pageSize: 50,
      sortBy: 'setupDetectedAtUtc',
      sortDirection: 'desc',
      search: undefined,
      direction: undefined,
      rawOutcome: undefined,
      confidenceDecision: undefined,
      confidenceMin: undefined,
      confidenceMax: undefined,
      riskDecision: undefined,
      riskMin: undefined,
      riskMax: undefined,
      profitableOnly: undefined,
      fromUtc: undefined,
      toUtc: undefined,
      quickFilter: undefined,
    })
  })

  it('sends the requested page and page size when paginating', () => {
    const query = buildCandidateQuery(baseState({ page: 3, pageSize: 100 }))
    expect(query.page).toBe(3)
    expect(query.pageSize).toBe(100)
  })

  it('converts numeric filter strings to numbers and omits blanks', () => {
    const query = buildCandidateQuery(baseState({ confidenceMin: '0.4', confidenceMax: '', riskMin: '10' }))
    expect(query.confidenceMin).toBe(0.4)
    expect(query.confidenceMax).toBeUndefined()
    expect(query.riskMin).toBe(10)
  })

  it('maps profitableOnly UI values to boolean query values', () => {
    expect(buildCandidateQuery(baseState({ profitableOnly: 'profitable' })).profitableOnly).toBe(true)
    expect(buildCandidateQuery(baseState({ profitableOnly: 'losing' })).profitableOnly).toBe(false)
    expect(buildCandidateQuery(baseState({ profitableOnly: '' })).profitableOnly).toBeUndefined()
  })

  it('sends the active quick filter and sort direction/column together', () => {
    const query = buildCandidateQuery(
      baseState({ quickFilter: 'ApprovedWinners', sortBy: 'rawNetPnl', sortDirection: 'asc', page: 2 }),
    )
    expect(query).toMatchObject({
      quickFilter: 'ApprovedWinners',
      sortBy: 'rawNetPnl',
      sortDirection: 'asc',
      page: 2,
    })
  })
})
