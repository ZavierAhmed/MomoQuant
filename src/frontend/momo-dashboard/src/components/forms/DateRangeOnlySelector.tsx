import { DateField } from '@/components/forms/fields';
import { validateDateRange } from '@/utils/formValidation';

type Props = {
  fromDate: string;
  toDate: string;
  onChange: (next: { fromDate: string; toDate: string }) => void;
  required?: boolean;
  maxRangeDays?: number;
  minDate?: string;
  maxDate?: string;
  helperText?: string;
  errors?: {
    fromDate?: string;
    toDate?: string;
  };
  disabled?: boolean;
};

export function DateRangeOnlySelector({
  fromDate,
  toDate,
  onChange,
  required,
  maxRangeDays,
  minDate,
  maxDate,
  helperText,
  errors,
  disabled,
}: Props) {
  const rangeErrors = validateDateRange(fromDate, toDate);
  const fromError = errors?.fromDate ?? rangeErrors.fromUtc;
  const toError = errors?.toDate ?? rangeErrors.toUtc;

  let maxRangeError: string | undefined;
  if (maxRangeDays && fromDate && toDate) {
    const from = new Date(`${fromDate}T00:00:00.000Z`);
    const to = new Date(`${toDate}T00:00:00.000Z`);
    const diffDays = Math.floor((to.getTime() - from.getTime()) / 86_400_000) + 1;
    if (diffDays > maxRangeDays) {
      maxRangeError = `Date range cannot exceed ${maxRangeDays} days.`;
    }
  }

  const hint =
    helperText ??
    'Select dates only. From date starts at 00:00:00 UTC and to date ends at 23:59:59.999 UTC.';

  return (
    <div className="grid gap-4 md:grid-cols-2">
      <DateField
        label="From Date (UTC)"
        value={fromDate}
        onChange={(value) => onChange({ fromDate: value, toDate })}
        required={required}
        error={fromError ?? maxRangeError}
        hint={hint}
        min={minDate}
        max={maxDate}
        disabled={disabled}
      />
      <DateField
        label="To Date (UTC)"
        value={toDate}
        onChange={(value) => onChange({ fromDate, toDate: value })}
        required={required}
        error={toError ?? maxRangeError}
        min={minDate}
        max={maxDate}
        disabled={disabled}
      />
    </div>
  );
}

export function dateRangeOnlyToUtc(fromDate: string, toDate: string) {
  return {
    fromUtc: fromDate ? `${fromDate}T00:00:00.000Z` : '',
    toUtc: toDate ? `${toDate}T23:59:59.999Z` : '',
  };
}
