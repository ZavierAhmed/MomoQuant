import { describe, expect, it } from 'vitest';
import type { Strategy } from '@/api/domainTypes';
import {
  CANONICAL_STRATEGY_CODES,
  filterActivePortfolioStrategies,
  filterArchivedStrategies,
  filterByCanonicalCodes,
  filterOperationallySelectableStrategies,
  filterStrategyLabNewRunStrategies,
  isArchivedStrategy,
  isOperationallySelectableStrategy,
} from '@/constants/canonicalStrategies';

function strategy(partial: Partial<Strategy> & Pick<Strategy, 'id' | 'code' | 'name'>): Strategy {
  return {
    description: '',
    isEnabled: true,
    version: '1.0.0',
    ...partial,
  };
}

describe('canonicalStrategies', () => {
  const activeCanonical = strategy({
    id: 1,
    code: CANONICAL_STRATEGY_CODES[0],
    name: 'MOMO Adaptive MTF',
    portfolioStatus: 'Active',
    isOperationallySelectable: true,
  });

  const disabledCanonical = strategy({
    id: 2,
    code: CANONICAL_STRATEGY_CODES[1],
    name: 'Price Structure',
    isEnabled: false,
    portfolioStatus: 'Active',
    isOperationallySelectable: true,
  });

  const archivedExplicit = strategy({
    id: 3,
    code: 'VOLATILITY_GATED_SUPERTREND_MOMENTUM',
    name: 'VG SuperTrend',
    portfolioStatus: 'Archived',
    isOperationallySelectable: false,
  });

  const legacyWithoutFields = strategy({
    id: 4,
    code: 'VWAP_MEAN_REVERSION',
    name: 'VWAP Mean Reversion',
  });

  it('treats only canonical codes as operationally selectable when backend fields are missing', () => {
    expect(isOperationallySelectableStrategy(activeCanonical)).toBe(true);
    expect(isOperationallySelectableStrategy(legacyWithoutFields)).toBe(false);
  });

  it('never treats archived strategies as operationally selectable', () => {
    expect(isOperationallySelectableStrategy(archivedExplicit)).toBe(false);
    expect(isArchivedStrategy(archivedExplicit)).toBe(true);
  });

  it('includes disabled canonical strategies only when showDisabled is true', () => {
    const all = [activeCanonical, disabledCanonical, archivedExplicit, legacyWithoutFields];
    expect(filterOperationallySelectableStrategies(all, false).map((s) => s.code)).toEqual([
      activeCanonical.code,
    ]);
    expect(filterOperationallySelectableStrategies(all, true).map((s) => s.code)).toEqual([
      activeCanonical.code,
      disabledCanonical.code,
    ]);
  });

  it('splits active portfolio from archived catalog entries', () => {
    const all = [activeCanonical, disabledCanonical, archivedExplicit, legacyWithoutFields];
    expect(filterActivePortfolioStrategies(all).map((s) => s.code)).toEqual([
      activeCanonical.code,
      disabledCanonical.code,
    ]);
    expect(filterArchivedStrategies(all).map((s) => s.code)).toEqual([
      archivedExplicit.code,
      legacyWithoutFields.code,
    ]);
  });

  it('limits strategy lab new-run codes to Price Structure only', () => {
    const labStrategies = filterStrategyLabNewRunStrategies([
      { code: CANONICAL_STRATEGY_CODES[0], name: 'A' },
      { code: CANONICAL_STRATEGY_CODES[1], name: 'B' },
      { code: 'VWAP_MEAN_REVERSION', name: 'Legacy' },
    ]);
    expect(labStrategies.map((s) => s.code)).toEqual([CANONICAL_STRATEGY_CODES[1]]);
  });

  it('filters code-only lists to canonical strategies for validation lab selectors', () => {
    const selectable = filterByCanonicalCodes([
      { code: CANONICAL_STRATEGY_CODES[2] },
      { code: 'EMA_PULLBACK' },
    ]);
    expect(selectable).toHaveLength(1);
    expect(selectable[0]?.code).toBe(CANONICAL_STRATEGY_CODES[2]);
  });
});
