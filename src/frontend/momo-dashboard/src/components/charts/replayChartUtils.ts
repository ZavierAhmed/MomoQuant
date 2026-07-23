import type { ReplayChartCandle, ReplayChartIndicator } from '@/api/domainTypes';

export function toChartTime(iso: string): number {
  return Math.floor(new Date(iso).getTime() / 1000);
}

export function findIndicatorForFrame(
  indicators: ReplayChartIndicator[],
  frameIndex: number,
): ReplayChartIndicator | undefined {
  return indicators.find((item) => item.frameIndex === frameIndex);
}

export function findCandleForFrame(
  candles: ReplayChartCandle[],
  frameIndex: number,
): ReplayChartCandle | undefined {
  return candles.find((item) => item.frameIndex === frameIndex);
}

export function hasIndicatorData(indicators: ReplayChartIndicator[]): boolean {
  return indicators.some(
    (item) =>
      item.ema20 != null ||
      item.ema50 != null ||
      item.ema200 != null ||
      item.vwap != null,
  );
}
