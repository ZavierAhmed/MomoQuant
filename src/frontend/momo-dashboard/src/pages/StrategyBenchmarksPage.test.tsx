import { describe, expect, it } from 'vitest';
import { CANONICAL_STRATEGY_CODES, isCanonicalStrategyCode } from '@/constants/canonicalStrategies';

describe('StrategyBenchmarks presets stay canonical', () => {
  it('canonical portfolio codes are exactly the three operational strategies', () => {
    expect(CANONICAL_STRATEGY_CODES).toEqual([
      'MOMO_ADAPTIVE_MTF_TREND_BREAKOUT',
      'PRICE_STRUCTURE_BREAKOUT_RETEST',
      'MOMO_VOLATILITY_RANGE_REVERSION',
    ]);
  });

  it('benchmark preset selection never includes archived strategy codes', () => {
    const referenceStrategies = [
      { id: 1, code: 'MOMO_ADAPTIVE_MTF_TREND_BREAKOUT', name: 'MOMO Adaptive' },
      { id: 2, code: 'PRICE_STRUCTURE_BREAKOUT_RETEST', name: 'Price Structure' },
      { id: 3, code: 'MOMO_VOLATILITY_RANGE_REVERSION', name: 'Range Reversion' },
      { id: 10, code: 'EMA_PULLBACK', name: 'EMA Pullback' },
      { id: 11, code: 'FOUR_HOUR_RANGE_REENTRY', name: '4H Range Re-entry' },
    ];

    const presetIds = referenceStrategies
      .filter((strategy) => isCanonicalStrategyCode(strategy.code))
      .map((strategy) => strategy.id);

    expect(presetIds).toEqual([1, 2, 3]);
    expect(presetIds).not.toContain(10);
    expect(presetIds).not.toContain(11);
  });
});
