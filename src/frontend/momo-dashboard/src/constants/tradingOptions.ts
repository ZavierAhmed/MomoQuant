import {
  SUPPORTED_MARKET_TIMEFRAMES,
  SUPPORTED_MARKET_TIMEFRAME_VALUES,
  timeframeLabel as marketTimeframeLabel,
} from '@/constants/timeframes';

export interface SelectOption<T extends string | number = string | number> {
  label: string;
  value: T;
  disabled?: boolean;
}

export const EXECUTION_TIMEFRAME_OPTIONS: SelectOption<string>[] = [
  { label: '3 minutes', value: '3m' },
  { label: '5 minutes', value: '5m' },
  { label: '15 minutes', value: '15m' },
];

export const TIMEFRAME_OPTIONS: SelectOption<string>[] = SUPPORTED_MARKET_TIMEFRAMES.map((tf) => ({
  label: tf.label,
  value: tf.value,
}));

export const TIMEFRAME_VALUES = SUPPORTED_MARKET_TIMEFRAME_VALUES;

export const EXECUTION_MODE_OPTIONS: SelectOption<string>[] = [
  { label: 'Market Fill', value: 'MarketFill' },
  { label: 'Maker Only', value: 'MakerOnly' },
  { label: 'Maker Then Cancel', value: 'MakerThenCancel' },
];

export const REPLAY_SPEED_OPTIONS: SelectOption<string>[] = [
  { label: 'Manual Step', value: 'ManualStep' },
  { label: '1x', value: '1x' },
  { label: '2x', value: '2x' },
  { label: '5x', value: '5x' },
  { label: '10x', value: '10x' },
];

export const PAPER_MODE_OPTIONS: SelectOption<string>[] = [
  { label: 'Historical Paper', value: 'HistoricalPaper' },
  { label: 'Live Paper', value: 'LivePaper' },
];

export const CURRENCY_OPTIONS: SelectOption<string>[] = [{ label: 'USDT', value: 'USDT' }];

export const DIRECTION_OPTIONS: SelectOption<string>[] = [
  { label: 'Long', value: 'Long' },
  { label: 'Short', value: 'Short' },
];

export const MARKET_REGIME_OPTIONS: SelectOption<string>[] = [
  { label: 'Trending', value: 'Trending' },
  { label: 'Ranging', value: 'Ranging' },
  { label: 'Breakout', value: 'Breakout' },
  { label: 'Reversal', value: 'Reversal' },
  { label: 'High Volatility', value: 'HighVolatility' },
  { label: 'Low Volatility', value: 'LowVolatility' },
  { label: 'Choppy', value: 'Choppy' },
  { label: 'Abnormal', value: 'Abnormal' },
  { label: 'Unknown', value: 'Unknown' },
];

export function timeframeLabel(value: number | string | null | undefined): string {
  if (value === null || value === undefined) {
    return '—';
  }

  if (typeof value === 'string') {
    return marketTimeframeLabel(value);
  }

  const map: Record<number, string> = {
    1: '1m',
    3: '3m',
    5: '5m',
    15: '15m',
    30: '30m',
    60: '1h',
    240: '4h',
    1440: '1d',
    10080: '1w',
  };

  return map[value] ?? String(value);
}

export function isSupportedTimeframe(value: string): boolean {
  return TIMEFRAME_VALUES.includes(value);
}
