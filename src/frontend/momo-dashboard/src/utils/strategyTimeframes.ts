import type { Strategy } from '@/api/domainTypes';
import { timeframeLabel } from '@/constants/timeframes';

export type TimeframeMode = 'StrategyDefault' | 'Custom';

export function intersectTimeframes(values: string[][]): string[] {
  if (values.length === 0) return [];
  return values.reduce((common, current) => common.filter((item) => current.includes(item)));
}

export function getStrategyPreferredTimeframe(strategy?: Strategy | null): string {
  return strategy?.preferredTimeframe ?? strategy?.allowedTimeframes?.[0] ?? '5m';
}

export function getStrategyAllowedTimeframes(strategy?: Strategy | null): string[] {
  return strategy?.allowedTimeframes?.length ? strategy.allowedTimeframes : [getStrategyPreferredTimeframe(strategy)];
}

export function getStrategyRequiredDataTimeframes(
  strategy?: Strategy | null,
  executionTimeframe?: string,
): string[] {
  const required = strategy?.requiredDataTimeframes ?? strategy?.requiredTimeframes ?? [];
  if (required.length > 0) {
    return required;
  }
  return executionTimeframe ? [executionTimeframe] : [getStrategyPreferredTimeframe(strategy)];
}

export function getCommonCustomTimeframes(strategies: Strategy[]): string[] {
  const allowed = strategies.map((strategy) => getStrategyAllowedTimeframes(strategy));
  return intersectTimeframes(allowed);
}

export function resolveExecutionTimeframes(
  strategies: Strategy[],
  mode: TimeframeMode,
  customTimeframes: string[],
): string[] {
  if (strategies.length === 0) return [];
  if (mode === 'Custom') {
    return customTimeframes;
  }
  if (strategies.length === 1) {
    return [getStrategyPreferredTimeframe(strategies[0])];
  }
  return strategies.map((strategy) => getStrategyPreferredTimeframe(strategy));
}

export function formatTimeframeList(values: string[]): string {
  return values.map((value) => timeframeLabel(value)).join(', ');
}

export function buildDefaultParameterSetName(
  strategyCode: string,
  symbolLabel: string,
  timeframe: string,
): string {
  const date = new Date().toISOString().slice(0, 10);
  return `${strategyCode} ${symbolLabel} ${timeframe} Optimized ${date}`;
}
