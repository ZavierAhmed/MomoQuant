import type { Strategy } from '@/api/domainTypes';

/** Authoritative operational portfolio for Milestone 23.1A. */
export const CANONICAL_STRATEGY_CODES = [
  'MOMO_ADAPTIVE_MTF_TREND_BREAKOUT',
  'PRICE_STRUCTURE_BREAKOUT_RETEST',
  'MOMO_VOLATILITY_RANGE_REVERSION',
] as const;

export type CanonicalStrategyCode = (typeof CANONICAL_STRATEGY_CODES)[number];

/** Only Price Structure supports new Strategy Laboratory runs in 23.1A. */
export const STRATEGY_LAB_NEW_RUN_CODES = ['PRICE_STRUCTURE_BREAKOUT_RETEST'] as const;

/** Default placeholder for advisory / risk sample payloads. */
export const DEFAULT_SAMPLE_STRATEGY_CODE: CanonicalStrategyCode = 'MOMO_ADAPTIVE_MTF_TREND_BREAKOUT';

export type StrategyPortfolioFields = Pick<Strategy, 'code' | 'portfolioStatus' | 'isOperationallySelectable'>;

export function isCanonicalStrategyCode(code: string): boolean {
  const normalized = code.trim().toUpperCase();
  return CANONICAL_STRATEGY_CODES.some((canonical) => canonical === normalized);
}

export function isStrategyLabNewRunCode(code: string): boolean {
  const normalized = code.trim().toUpperCase();
  return STRATEGY_LAB_NEW_RUN_CODES.some((labCode) => labCode === normalized);
}

export function isArchivedStrategy(strategy: StrategyPortfolioFields): boolean {
  if (strategy.portfolioStatus === 'Archived') return true;
  if (strategy.portfolioStatus === 'Active') return false;
  return !isCanonicalStrategyCode(strategy.code);
}

export function isActivePortfolioStrategy(strategy: StrategyPortfolioFields): boolean {
  if (strategy.portfolioStatus === 'Active') return true;
  if (strategy.portfolioStatus === 'Archived') return false;
  return isCanonicalStrategyCode(strategy.code);
}

export function isOperationallySelectableStrategy(strategy: StrategyPortfolioFields): boolean {
  if (strategy.isOperationallySelectable === true) return true;
  if (strategy.isOperationallySelectable === false) return false;
  return isCanonicalStrategyCode(strategy.code);
}

export function filterActivePortfolioStrategies<T extends StrategyPortfolioFields>(strategies: T[]): T[] {
  return strategies.filter(isActivePortfolioStrategy);
}

export function filterArchivedStrategies<T extends StrategyPortfolioFields>(strategies: T[]): T[] {
  return strategies.filter(isArchivedStrategy);
}

export function filterOperationallySelectableStrategies<T extends Strategy & StrategyPortfolioFields>(
  strategies: T[],
  showDisabled: boolean,
): T[] {
  return strategies.filter((strategy) => {
    if (!isOperationallySelectableStrategy(strategy)) return false;
    if (!showDisabled && !strategy.isEnabled) return false;
    return true;
  });
}

export function filterStrategyLabNewRunStrategies<T extends { code: string }>(strategies: T[]): T[] {
  return strategies.filter((strategy) => isStrategyLabNewRunCode(strategy.code));
}

export function filterByCanonicalCodes<T extends { code: string }>(strategies: T[]): T[] {
  return strategies.filter((strategy) => isCanonicalStrategyCode(strategy.code));
}
