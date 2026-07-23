export function validateRequired(value: unknown, message: string): string | undefined {
  if (value === undefined || value === null || value === '') {
    return message;
  }

  return undefined;
}

export function validateDateRange(fromUtc: string, toUtc: string): Record<string, string> {
  const errors: Record<string, string> = {};

  if (!fromUtc) {
    errors.fromUtc = 'From Date is required.';
  }

  if (!toUtc) {
    errors.toUtc = 'To Date is required.';
  }

  if (fromUtc && toUtc) {
    const from = new Date(fromUtc);
    const to = new Date(toUtc);

    if (Number.isNaN(from.getTime()) || Number.isNaN(to.getTime())) {
      errors.fromUtc = errors.fromUtc ?? 'Invalid date range.';
    } else if (from > to) {
      errors.toUtc = 'From Date must be before or equal to To Date.';
    }
  }

  return errors;
}

export function validateHistoricalPaperDates(
  mode: string,
  fromUtc: string,
  toUtc: string,
): Record<string, string> {
  if (mode !== 'HistoricalPaper') {
    return {};
  }

  return validateDateRange(fromUtc, toUtc);
}

export type PaperSessionAction = 'start' | 'pause' | 'resume' | 'stop';

export function getPaperSessionActions(status: string | number | undefined): PaperSessionAction[] {
  const normalized = String(status ?? '').toLowerCase();

  if (normalized === 'created') {
    return ['start'];
  }

  if (normalized === 'running') {
    return ['pause', 'stop'];
  }

  if (normalized === 'paused') {
    return ['resume', 'stop'];
  }

  return [];
}

export function paperSessionActionLabel(action: PaperSessionAction): string {
  switch (action) {
    case 'start':
      return 'Start';
    case 'pause':
      return 'Pause';
    case 'resume':
      return 'Resume';
    case 'stop':
      return 'Stop';
  }
}
