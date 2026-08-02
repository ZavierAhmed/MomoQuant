import { describe, expect, it } from 'vitest';
import type { StrategyParameterSet } from '@/api/strategyResearchApi';
import {
  DEPLOYMENT_SIMULATION_EXPLANATION,
  PAPER_QUALIFICATION_VALUE_FALLBACK,
  RESEARCH_PAPER_EXPLANATION,
  deploymentQualificationDisplayRows,
  deploymentPaperSelectionErrors,
  filterDeploymentQualifiedParameterSets,
  isDeploymentPaperSelectionComplete,
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

  it('keeps research and deployment simulation distinct and explains that no real orders are placed', () => {
    expect(RESEARCH_PAPER_EXPLANATION).toContain('experimentation');
    expect(DEPLOYMENT_SIMULATION_EXPLANATION).toContain('rechecks its evidence before every start or resume');
    expect(DEPLOYMENT_SIMULATION_EXPLANATION).toContain('no real orders');
  });

  it('uses an uncorrupted fallback for missing deployment qualification evidence', () => {
    expect(PAPER_QUALIFICATION_VALUE_FALLBACK).toBe('Not available');
    expect(PAPER_QUALIFICATION_VALUE_FALLBACK).not.toContain(
      String.fromCharCode(0x00e2, 0x20ac, 0x201d),
    );
    const rows = deploymentQualificationDisplayRows({});
    expect(rows).toHaveLength(8);
    expect(rows.map((row) => row.value)).toEqual(Array(8).fill('Not available'));
  });

  it('requires the exact deployment-simulation selection before submission', () => {
    const incomplete = deploymentPaperSelectionErrors({
      mode: 'HistoricalPaper',
      strategyCount: 2,
      symbolCount: 0,
      timeframeCount: 2,
      parameterSetId: '',
    });
    expect(Object.keys(incomplete)).toEqual(['mode', 'strategyIds', 'symbolIds', 'timeframes', 'parameterSetId']);
    expect(isDeploymentPaperSelectionComplete({
      mode: 'LivePaper',
      strategyCount: 1,
      symbolCount: 1,
      timeframeCount: 1,
      parameterSetId: 42,
    })).toBe(true);
  });

  it('excludes research-only and historical sets from deployment selection', () => {
    const qualified = parameterSet({
      id: 2,
      name: 'Published set',
      qualificationStatus: 'DeploymentQualified',
      isDeploymentQualified: true,
      qualificationBlockingReasons: [],
    });
    const historical = parameterSet({
      id: 3,
      qualificationStatus: 'HistoricalNotEvaluated',
      qualificationBlockingReasons: ['PARAMETER_SET_HISTORICAL_NOT_EVALUATED'],
    });

    expect(filterDeploymentQualifiedParameterSets([parameterSet(), qualified, historical], true)).toEqual([qualified]);
    expect(filterDeploymentQualifiedParameterSets([parameterSet(), qualified, historical], false)).toHaveLength(3);
  });
});
