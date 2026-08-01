import { describe, expect, it } from 'vitest';
import type { StrategyParameterSet } from '@/api/strategyResearchApi';
import {
  parameterSetApprovalLabel,
  parameterSetQualificationExplanation,
  parameterSetQualificationLabel,
} from './parameterSetQualification';

function parameterSet(overrides: Partial<StrategyParameterSet> = {}): StrategyParameterSet {
  return {
    id: 1,
    name: 'Research set',
    strategyCode: 'MOMO_ADAPTIVE_MTF_TREND_BREAKOUT',
    timeframe: '15m',
    parameters: {},
    source: 'Manual',
    isApproved: true,
    qualificationStatus: 'ResearchOnly',
    approvalScope: 'Research',
    isDeploymentQualified: false,
    qualificationBlockingReasons: ['PARAMETER_SET_RESEARCH_ONLY'],
    createdAtUtc: '2026-08-01T00:00:00Z',
    ...overrides,
  };
}

describe('parameter-set approval and qualification wording', () => {
  it('describes research approval without implying deployment qualification', () => {
    const set = parameterSet();

    expect(parameterSetApprovalLabel(set)).toBe('Research approved');
    expect(parameterSetQualificationLabel(set)).toBe('Research-only');
    expect(parameterSetQualificationExplanation(set)).toContain('Not deployment qualified');
    expect(parameterSetQualificationExplanation(set)).toContain('controlled research');
  });

  it('distinguishes historical rows that have not been evaluated', () => {
    const set = parameterSet({
      qualificationStatus: 'HistoricalNotEvaluated',
      qualificationBlockingReasons: ['PARAMETER_SET_HISTORICAL_NOT_EVALUATED'],
    });

    expect(parameterSetQualificationLabel(set)).toBe('Historical — not evaluated');
    expect(parameterSetQualificationExplanation(set)).toContain('historical parameter set');
  });

  it('does not describe an unapproved set as research approved', () => {
    expect(parameterSetApprovalLabel(parameterSet({ isApproved: false }))).toBe('Not research approved');
  });
});
