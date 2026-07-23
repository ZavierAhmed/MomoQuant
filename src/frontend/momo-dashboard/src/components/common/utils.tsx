export function JsonBlock({ value }: { value: unknown }) {
  return (
    <pre className="overflow-x-auto rounded-xl border border-slate-800 bg-slate-950 p-4 text-xs text-slate-300">
      {JSON.stringify(value, null, 2)}
    </pre>
  );
}

export function formatDate(value?: string | null): string {
  if (!value) {
    return '—';
  }

  return new Date(value).toLocaleString();
}

export function formatNumber(value?: number | null, digits = 2): string {
  if (value === undefined || value === null || Number.isNaN(value)) {
    return '—';
  }

  return value.toLocaleString(undefined, { maximumFractionDigits: digits });
}

export function formatExpectancyR(value?: number | null, digits = 4): string {
  if (value === undefined || value === null || Number.isNaN(value)) {
    return '—';
  }

  return `${value.toLocaleString(undefined, { maximumFractionDigits: digits })} R/trade`;
}

export function formatVerdictLabel(decision?: string | null): string {
  if (!decision) return 'Pending';
  if (decision === 'FailedNegativeTrainingExpectancy') return 'Failed: Negative Training Expectancy';
  if (decision === 'FailedNegativeValidationExpectancy') return 'Failed: Negative Validation Expectancy';
  if (decision === 'FailedNoTrainingTrialPassedGuardrails') return 'Failed: No Training Trial Passed Guardrails';
  return decision.replace(/([a-z])([A-Z])/g, '$1 $2');
}

export function dedupeFailureReasons(raw?: string | null): string[] {
  if (!raw) return [];
  try {
    const parsed = JSON.parse(raw) as unknown;
    if (!Array.isArray(parsed)) return [];
    const seen = new Set<string>();
    const out: string[] = [];
    for (const item of parsed) {
      const obj = item as { ruleKey?: string; reason?: string; message?: string };
      const key = obj.ruleKey ?? obj.reason ?? obj.message ?? String(item);
      if (seen.has(key)) continue;
      seen.add(key);
      out.push(obj.reason ?? obj.message ?? String(item));
    }
    return out;
  } catch {
    return [raw];
  }
}
