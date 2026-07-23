import type { ReactNode } from 'react';
import type { SelectOption } from '@/constants/tradingOptions';

export const fieldInputClass =
  'mt-1 block w-full rounded-lg border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100 focus:border-slate-500 focus:outline-none';

export const fieldLabelClass = 'text-sm font-medium text-slate-300';

function FieldShell({
  label,
  htmlFor,
  error,
  hint,
  required,
  children,
}: {
  label: string;
  htmlFor?: string;
  error?: string;
  hint?: string;
  required?: boolean;
  children: ReactNode;
}) {
  return (
    <div>
      <label htmlFor={htmlFor} className={fieldLabelClass}>
        {label}
        {required ? <span className="text-rose-400"> *</span> : null}
      </label>
      {children}
      {hint ? <p className="mt-1 text-xs text-slate-500">{hint}</p> : null}
      {error ? <p className="mt-1 text-xs text-rose-400">{error}</p> : null}
    </div>
  );
}

export function TextField({
  label,
  value,
  onChange,
  error,
  hint,
  required,
  placeholder,
  type = 'text',
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  error?: string;
  hint?: string;
  required?: boolean;
  placeholder?: string;
  type?: 'text' | 'email' | 'password';
}) {
  const id = label.replace(/\s+/g, '-').toLowerCase();

  return (
    <FieldShell label={label} htmlFor={id} error={error} hint={hint} required={required}>
      <input
        id={id}
        type={type}
        value={value}
        placeholder={placeholder}
        onChange={(event) => onChange(event.target.value)}
        className={fieldInputClass}
      />
    </FieldShell>
  );
}

export function NumberField({
  label,
  value,
  onChange,
  error,
  hint,
  required,
  step,
  min,
  max,
}: {
  label: string;
  value: number | '';
  onChange: (value: number | '') => void;
  error?: string;
  hint?: string;
  required?: boolean;
  step?: number;
  min?: number;
  max?: number;
}) {
  const id = label.replace(/\s+/g, '-').toLowerCase();

  return (
    <FieldShell label={label} htmlFor={id} error={error} hint={hint} required={required}>
      <input
        id={id}
        type="number"
        step={step}
        min={min}
        max={max}
        value={value}
        onChange={(event) => onChange(event.target.value === '' ? '' : Number(event.target.value))}
        className={fieldInputClass}
      />
    </FieldShell>
  );
}

export function DateField({
  label,
  value,
  onChange,
  error,
  hint,
  required,
  min,
  max,
  disabled,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  error?: string;
  hint?: string;
  required?: boolean;
  min?: string;
  max?: string;
  disabled?: boolean;
}) {
  const id = label.replace(/\s+/g, '-').toLowerCase();
  const defaultHint = 'Dates are interpreted in UTC. From Date starts at 00:00:00 UTC and To Date ends at 23:59:59 UTC.';

  return (
    <FieldShell label={label} htmlFor={id} error={error} hint={hint ?? defaultHint} required={required}>
      <input
        id={id}
        type="date"
        value={value}
        min={min}
        max={max}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value)}
        className={fieldInputClass}
      />
    </FieldShell>
  );
}

export function DateTimeField({
  label,
  value,
  onChange,
  error,
  hint,
  required,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  error?: string;
  hint?: string;
  required?: boolean;
}) {
  const id = label.replace(/\s+/g, '-').toLowerCase();

  return (
    <FieldShell label={label} htmlFor={id} error={error} hint={hint} required={required}>
      <input
        id={id}
        type="datetime-local"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className={fieldInputClass}
      />
    </FieldShell>
  );
}

export function SelectField<T extends string | number>({
  label,
  value,
  onChange,
  options,
  error,
  hint,
  required,
  placeholder = 'Select…',
  loading,
  disabled,
}: {
  label: string;
  value: T | '';
  onChange: (value: T | '') => void;
  options: SelectOption<T>[];
  error?: string;
  hint?: string;
  required?: boolean;
  placeholder?: string;
  loading?: boolean;
  disabled?: boolean;
}) {
  const id = label.replace(/\s+/g, '-').toLowerCase();

  return (
    <FieldShell label={label} htmlFor={id} error={error} hint={hint} required={required}>
      <select
        id={id}
        disabled={disabled || loading}
        value={value === '' ? '' : String(value)}
        onChange={(event) => {
          if (!event.target.value) {
            onChange('');
            return;
          }

          const selected = options.find((option) => String(option.value) === event.target.value);
          onChange((selected?.value ?? event.target.value) as T);
        }}
        className={fieldInputClass}
      >
        <option value="">{loading ? 'Loading…' : placeholder}</option>
        {options.map((option) => (
          <option key={String(option.value)} value={String(option.value)} disabled={option.disabled}>
            {option.label}
          </option>
        ))}
      </select>
    </FieldShell>
  );
}

export function MultiSelectField<T extends string | number>({
  label,
  values,
  onChange,
  options,
  error,
  hint,
  required,
  loading,
  emptyMessage = 'No options available.',
}: {
  label: string;
  values: T[];
  onChange: (values: T[]) => void;
  options: SelectOption<T>[];
  error?: string;
  hint?: string;
  required?: boolean;
  loading?: boolean;
  emptyMessage?: string;
}) {
  function toggle(optionValue: T) {
    if (values.includes(optionValue)) {
      onChange(values.filter((value) => value !== optionValue));
      return;
    }

    onChange([...values, optionValue]);
  }

  return (
    <FieldShell label={label} error={error} hint={hint} required={required}>
      <div className="mt-1 max-h-40 overflow-y-auto rounded-lg border border-slate-700 bg-slate-950 p-2">
        {loading ? <p className="px-2 py-1 text-xs text-slate-500">Loading options…</p> : null}
        {!loading && options.length === 0 ? (
          <p className="px-2 py-1 text-xs text-slate-500">{emptyMessage}</p>
        ) : null}
        {!loading
          ? options.map((option) => (
              <label
                key={String(option.value)}
                className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-sm text-slate-200 hover:bg-slate-900"
              >
                <input
                  type="checkbox"
                  checked={values.includes(option.value)}
                  disabled={option.disabled}
                  onChange={() => toggle(option.value)}
                  className="rounded border-slate-600"
                />
                <span>{option.label}</span>
              </label>
            ))
          : null}
      </div>
    </FieldShell>
  );
}

export function CheckboxField({
  label,
  checked,
  onChange,
  hint,
}: {
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
  hint?: string;
}) {
  return (
    <label className="flex items-start gap-2 rounded-lg border border-slate-800 bg-slate-950/40 px-3 py-2">
      <input
        type="checkbox"
        checked={checked}
        onChange={(event) => onChange(event.target.checked)}
        className="mt-0.5 rounded border-slate-600"
      />
      <span>
        <span className="text-sm font-medium text-slate-300">{label}</span>
        {hint ? <span className="mt-0.5 block text-xs text-slate-500">{hint}</span> : null}
      </span>
    </label>
  );
}
