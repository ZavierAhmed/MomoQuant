import { validateDateRange } from '@/utils/formValidation';
import { dateOnlyToUtcEnd, dateOnlyToUtcStart, toUtcIsoString } from '@/utils/datetime';

export { validateDateRange, validateRequired, getPaperSessionActions, paperSessionActionLabel, validateHistoricalPaperDates } from '@/utils/formValidation';

export { dateOnlyToUtcStart, dateOnlyToUtcEnd } from '@/utils/datetime';

export function buildUtcDateRange(fromDate: string, toDate: string) {
  return {
    fromUtc: dateOnlyToUtcStart(fromDate),
    toUtc: dateOnlyToUtcEnd(toDate),
  };
}

export function buildUtcRange(fromLocal: string, toLocal: string) {
  if (/^\d{4}-\d{2}-\d{2}$/.test(fromLocal) && /^\d{4}-\d{2}-\d{2}$/.test(toLocal)) {
    return buildUtcDateRange(fromLocal, toLocal);
  }

  return {
    fromUtc: toUtcIsoString(fromLocal),
    toUtc: toUtcIsoString(toLocal),
  };
}

export function validateUtcRangeFields(fromLocal: string, toLocal: string): Record<string, string> {
  const errors = validateDateRange(fromLocal, toLocal);
  if (fromLocal && !isValidDateInput(fromLocal)) {
    errors.fromUtc = 'Invalid start date.';
  }
  if (toLocal && !isValidDateInput(toLocal)) {
    errors.toUtc = 'Invalid end date.';
  }
  return errors;
}

function isValidDateInput(value: string): boolean {
  if (/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    return !Number.isNaN(new Date(`${value}T00:00:00.000Z`).getTime());
  }

  return !!toUtcIsoString(value);
}
