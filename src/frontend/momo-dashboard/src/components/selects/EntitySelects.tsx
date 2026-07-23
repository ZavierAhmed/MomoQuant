import { SelectField, MultiSelectField } from '@/components/forms/fields';
import type { SelectOption } from '@/constants/tradingOptions';

type SelectProps = {
  label: string;
  value: number | '';
  onChange: (value: number | '') => void;
  options: SelectOption<number>[];
  loading?: boolean;
  required?: boolean;
  error?: string;
  placeholder?: string;
  hint?: string;
};

export function ExchangeSelect(props: SelectProps) {
  return <SelectField {...props} />;
}

export function SymbolSelect(props: SelectProps) {
  return <SelectField {...props} />;
}

export function StrategySelect(props: SelectProps) {
  return <SelectField {...props} />;
}

export function RiskProfileSelect(props: SelectProps) {
  return <SelectField {...props} />;
}

export function PaperAccountSelect(props: SelectProps) {
  return <SelectField {...props} />;
}

export function SymbolMultiSelect({
  label,
  values,
  onChange,
  options,
  loading,
  required,
  error,
}: {
  label: string;
  values: number[];
  onChange: (values: number[]) => void;
  options: SelectOption<number>[];
  loading?: boolean;
  required?: boolean;
  error?: string;
}) {
  return (
    <MultiSelectField
      label={label}
      values={values}
      onChange={onChange}
      options={options}
      loading={loading}
      required={required}
      error={error}
    />
  );
}
