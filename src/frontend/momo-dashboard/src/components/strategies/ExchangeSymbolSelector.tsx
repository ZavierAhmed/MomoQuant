import { useEffect } from 'react';
import { MultiSelectField, SelectField } from '@/components/forms/fields';
import { useReferenceData } from '@/hooks/useReferenceData';
import { useExchangeSymbols } from '@/hooks/useExchangeSymbols';

type Props = {
  selectedExchangeId: number | '';
  selectedSymbolIds: number[];
  onExchangeChange: (exchangeId: number | '') => void;
  onSymbolsChange: (symbolIds: number[]) => void;
  multiSelect?: boolean;
  required?: boolean;
  disabled?: boolean;
  helperText?: string;
  exchangeError?: string;
  symbolsError?: string;
};

export function ExchangeSymbolSelector({
  selectedExchangeId,
  selectedSymbolIds,
  onExchangeChange,
  onSymbolsChange,
  multiSelect = true,
  required,
  disabled,
  helperText,
  exchangeError,
  symbolsError,
}: Props) {
  const reference = useReferenceData(selectedExchangeId || null);
  const exchangeSymbols = useExchangeSymbols(selectedExchangeId || null);

  useEffect(() => {
    if (!selectedExchangeId || selectedSymbolIds.length === 0) return;
    const allowed = new Set(exchangeSymbols.symbols.map((symbol) => symbol.id));
    const filtered = selectedSymbolIds.filter((id) => allowed.has(id));
    if (filtered.length !== selectedSymbolIds.length) {
      onSymbolsChange(filtered);
    }
  }, [selectedExchangeId, exchangeSymbols.symbols, selectedSymbolIds, onSymbolsChange]);

  function handleExchangeChange(value: number | '') {
    onExchangeChange(value);
    onSymbolsChange([]);
  }

  const symbolHelper =
    helperText ??
    (!selectedExchangeId
      ? 'Select an exchange first to load enabled symbols.'
      : undefined);

  const symbolField = multiSelect ? (
    <MultiSelectField
      label="Symbols"
      values={selectedSymbolIds}
      onChange={onSymbolsChange}
      options={exchangeSymbols.symbolOptions}
      loading={exchangeSymbols.loading}
      required={required}
      error={symbolsError}
      hint={symbolHelper}
      emptyMessage={
        selectedExchangeId
          ? 'No enabled symbols found for this exchange.'
          : 'Select an exchange first to load enabled symbols.'
      }
    />
  ) : (
    <SelectField
      label="Symbol"
      value={selectedSymbolIds[0] ?? ''}
      onChange={(value) => onSymbolsChange(value === '' ? [] : [Number(value)])}
      options={exchangeSymbols.symbolOptions}
      loading={exchangeSymbols.loading}
      required={required}
      error={symbolsError}
      hint={symbolHelper}
      disabled={!selectedExchangeId || disabled}
      placeholder={selectedExchangeId ? 'Select symbol…' : 'Select exchange first…'}
    />
  );

  return (
    <>
      <SelectField
        label="Exchange"
        value={selectedExchangeId}
        onChange={handleExchangeChange}
        options={reference.exchangeOptions}
        loading={reference.loading}
        required={required}
        error={exchangeError}
        disabled={disabled}
      />
      {selectedExchangeId ? symbolField : null}
    </>
  );
}
