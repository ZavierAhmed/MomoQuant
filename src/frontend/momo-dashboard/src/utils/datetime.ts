/** Convert date-only input (YYYY-MM-DD) to UTC start of day. */
export function dateOnlyToUtcStart(dateString: string): string {
  if (!dateString) {
    return '';
  }

  return `${dateString}T00:00:00.000Z`;
}

/** Convert date-only input (YYYY-MM-DD) to UTC end of day. */
export function dateOnlyToUtcEnd(dateString: string): string {
  if (!dateString) {
    return '';
  }

  return `${dateString}T23:59:59.999Z`;
}

/** Convert datetime-local input value to UTC ISO string ending in Z. */
export function toUtcIsoString(value: string): string {
  if (!value) {
    return '';
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return '';
  }

  return date.toISOString();
}

/** Convert UTC ISO string to datetime-local input value (browser local display). */
export function fromUtcIsoString(value?: string | null): string {
  if (!value) {
    return '';
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return '';
  }

  const pad = (part: number) => String(part).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

export function isValidUtcRange(fromUtc: string, toUtc: string): boolean {
  if (!fromUtc || !toUtc) {
    return false;
  }

  const from = new Date(fromUtc);
  const to = new Date(toUtc);
  return !Number.isNaN(from.getTime()) && !Number.isNaN(to.getTime()) && from < to;
}
