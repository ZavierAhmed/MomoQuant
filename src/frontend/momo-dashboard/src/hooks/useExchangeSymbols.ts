import { useMemo } from 'react';
import { exchangesApi } from '@/api/exchangesApi';
import { useAsync } from '@/hooks/useAsync';

export function useExchangeSymbols(exchangeId?: number | null) {
  const symbols = useAsync(
    () =>
      exchangeId
        ? exchangesApi.listSymbols(exchangeId, true)
        : Promise.resolve([]),
    [exchangeId],
  );

  const symbolOptions = useMemo(() => {
    const seen = new Set<string>();
    return (symbols.data ?? [])
      .filter((symbol) => {
        const key = symbol.symbol.trim().toUpperCase();
        if (seen.has(key)) return false;
        seen.add(key);
        return true;
      })
      .map((symbol) => ({
        label: symbol.displayName,
        value: symbol.id,
      }));
  }, [symbols.data]);

  return {
    symbols: symbols.data ?? [],
    symbolOptions,
    loading: symbols.loading,
    error: symbols.error,
    reload: symbols.reload,
  };
}
