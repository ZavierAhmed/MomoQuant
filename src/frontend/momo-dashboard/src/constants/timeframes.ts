export interface MarketTimeframeOption {
  value: string;
  label: string;
  minutes: number;
}

/** Canonical Binance Futures public candle intervals used across SK and market data UIs. */
export const SUPPORTED_MARKET_TIMEFRAMES: MarketTimeframeOption[] = [
  { value: '1m', label: '1 minute', minutes: 1 },
  { value: '3m', label: '3 minutes', minutes: 3 },
  { value: '5m', label: '5 minutes', minutes: 5 },
  { value: '15m', label: '15 minutes', minutes: 15 },
  { value: '30m', label: '30 minutes', minutes: 30 },
  { value: '1h', label: '1 hour', minutes: 60 },
  { value: '4h', label: '4 hours', minutes: 240 },
  { value: '1d', label: '1 day', minutes: 1440 },
  { value: '1w', label: '1 week', minutes: 10080 },
];

export const SUPPORTED_MARKET_TIMEFRAME_VALUES = SUPPORTED_MARKET_TIMEFRAMES.map((tf) => tf.value);

/** @deprecated Use SUPPORTED_MARKET_TIMEFRAMES — kept for imports that expect SelectOption shape. */
export const TIMEFRAME_OPTIONS = SUPPORTED_MARKET_TIMEFRAMES.map((tf) => ({
  value: tf.value,
  label: tf.label,
}));

export const RECOMMENDED_SK_TIMEFRAME_PAIRS: Array<{ higher: string; primary: string }> = [
  { higher: '1d', primary: '4h' },
  { higher: '4h', primary: '1h' },
  { higher: '1h', primary: '15m' },
  { higher: '30m', primary: '5m' },
  { higher: '15m', primary: '1m' },
];

export const HTF_LTF_VALIDATION_MESSAGE =
  'The bigger picture chart must be higher than the analysis chart. Example: 4h as HTF and 1h as LTF.';

export function getTimeframeMinutes(value: string): number | null {
  return SUPPORTED_MARKET_TIMEFRAMES.find((tf) => tf.value === value)?.minutes ?? null;
}

export function isSupportedMarketTimeframe(value: string): boolean {
  return SUPPORTED_MARKET_TIMEFRAME_VALUES.includes(value);
}

export function isValidHtfLtfPair(higherTimeframe: string, primaryTimeframe: string): boolean {
  const higherMinutes = getTimeframeMinutes(higherTimeframe);
  const primaryMinutes = getTimeframeMinutes(primaryTimeframe);
  if (higherMinutes === null || primaryMinutes === null) {
    return false;
  }
  return higherMinutes > primaryMinutes;
}

export function getHtfLtfValidationError(higherTimeframe: string, primaryTimeframe: string): string | null {
  if (!isSupportedMarketTimeframe(higherTimeframe) || !isSupportedMarketTimeframe(primaryTimeframe)) {
    return 'Select supported timeframes from the list.';
  }
  if (!isValidHtfLtfPair(higherTimeframe, primaryTimeframe)) {
    return `Invalid pair: ${higherTimeframe} cannot be the bigger picture chart when ${primaryTimeframe} is the analysis chart. ${HTF_LTF_VALIDATION_MESSAGE}`;
  }
  return null;
}

export function getSelectedPairHelperText(higherTimeframe: string, primaryTimeframe: string): string {
  const error = getHtfLtfValidationError(higherTimeframe, primaryTimeframe);
  if (error) {
    return error;
  }
  return `Using ${higherTimeframe} for direction and ${primaryTimeframe} for SK decisions.`;
}

export function timeframeLabel(value: string): string {
  return SUPPORTED_MARKET_TIMEFRAMES.find((tf) => tf.value === value)?.label ?? value;
}

const TIMEFRAME_ALIASES: Record<string, string> = {
  '1 minute': '1m',
  '1 minutes': '1m',
  '3 minute': '3m',
  '3 minutes': '3m',
  '5 minute': '5m',
  '5 minutes': '5m',
  '15 minute': '15m',
  '15 minutes': '15m',
  '30 minute': '30m',
  '30 minutes': '30m',
  '1 hour': '1h',
  '1 hours': '1h',
  '60m': '1h',
  '4 hour': '4h',
  '4 hours': '4h',
  '1 day': '1d',
  '1 week': '1w',
};

export function normalizeTimeframe(value: string): string {
  const trimmed = value.trim();
  if (SUPPORTED_MARKET_TIMEFRAME_VALUES.includes(trimmed)) {
    return trimmed;
  }

  const alias = TIMEFRAME_ALIASES[trimmed.toLowerCase()];
  if (alias) {
    return alias;
  }

  throw new Error(
    `Unsupported timeframe '${value}'. Supported: ${SUPPORTED_MARKET_TIMEFRAME_VALUES.join(', ')}`,
  );
}

export function tryNormalizeTimeframe(value: string): string | null {
  try {
    return normalizeTimeframe(value);
  } catch {
    return null;
  }
}
