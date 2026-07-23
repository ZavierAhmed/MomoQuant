import { ApiClientError } from '@/api/apiClient';
import type { ApiError } from '@/api/types';

export interface ParsedApiError {
  title: string;
  message: string;
  fieldErrors: Record<string, string>;
  traceId?: string;
}

const TIMEFRAME_HINT =
  'Timeframe is invalid. Supported values: 1m, 3m, 5m, 15m, 30m, 1h, 4h, 1d, 1w.';

const NO_CANDLE_HINT =
  'No candle data exists for this symbol/timeframe/date range. Import candles first from Market Watch.';

function mapFieldMessage(field: string, message: string): string {
  const normalized = field.toLowerCase();
  if (normalized.includes('validationmode') || field.toLowerCase() === 'validationmode') {
    return 'Validation request failed: validationMode value is invalid.';
  }
  if (normalized.includes('timeframe')) {
    return TIMEFRAME_HINT;
  }
  if (message.toLowerCase().includes('candle') && message.toLowerCase().includes('not')) {
    return NO_CANDLE_HINT;
  }
  return message;
}

export function parseApiClientError(error: unknown): ParsedApiError {
  if (error instanceof ApiClientError) {
    const fieldErrors: Record<string, string> = {};
    for (const item of error.errors ?? []) {
      if (item.field) {
        fieldErrors[item.field] = mapFieldMessage(item.field, item.message);
      }
    }

    let message = error.message || 'Request failed.';
    if (error.status === 400 && Object.keys(fieldErrors).length > 0) {
      message = Object.values(fieldErrors).join(' ');
    } else if (error.status === 400 && message === 'Bad Request') {
      message = 'Validation failed. Check the form fields and try again.';
    } else if (error.status === 404) {
      const lower = message.toLowerCase();
      if (lower.includes('not found') || lower === 'not found' || !error.message) {
        message =
          'Strategy Laboratory API is unavailable at the expected path. Confirm /api/v1/strategy-lab is registered and tables are migrated.';
      }
    }

    if (message.toLowerCase().includes('no candle') || message.toLowerCase().includes('candle data')) {
      message = NO_CANDLE_HINT;
    }

    return {
      title: error.status === 400 ? 'Validation Error' : 'Request Failed',
      message,
      fieldErrors,
    };
  }

  if (error instanceof Error) {
    return {
      title: 'Request Failed',
      message: error.message,
      fieldErrors: {},
    };
  }

  return {
    title: 'Request Failed',
    message: 'An unexpected error occurred.',
    fieldErrors: {},
  };
}

export function applyFieldErrorsToForm(
  fieldErrors: Record<string, string>,
  mapping: Record<string, string>,
): Record<string, string> {
  const result: Record<string, string> = {};
  for (const [apiField, message] of Object.entries(fieldErrors)) {
    const formField = mapping[apiField] ?? apiField;
    result[formField] = message;
  }
  return result;
}

export function errorsFromApiList(errors?: ApiError[]): Record<string, string> {
  const result: Record<string, string> = {};
  for (const item of errors ?? []) {
    if (item.field) {
      result[item.field] = mapFieldMessage(item.field, item.message);
    }
  }
  return result;
}
