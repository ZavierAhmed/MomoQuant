import { useMemo } from 'react';
import { exchangesApi } from '@/api/exchangesApi';
import { paperTradingApi } from '@/api/paperTradingApi';
import { riskApi } from '@/api/riskApi';
import { strategiesApi } from '@/api/strategiesApi';
import { symbolsApi } from '@/api/symbolsApi';
import { buildSymbolOptionLabel, dedupeSymbolsByName, enabledStrategiesFirst, filterSymbolsByExchange } from '@/utils/referenceLookups';
import { useAsync } from '@/hooks/useAsync';

export function useReferenceData(exchangeId?: number | null) {
  const exchanges = useAsync(() => exchangesApi.list({ page: 1, pageSize: 100 }), []);
  const symbols = useAsync(() => symbolsApi.list({ page: 1, pageSize: 500, exchangeId: exchangeId ?? undefined }), [exchangeId]);
  const allSymbols = useAsync(() => symbolsApi.list({ page: 1, pageSize: 500 }), []);
  const strategies = useAsync(() => strategiesApi.list(), []);
  const riskProfiles = useAsync(() => riskApi.listProfiles(), []);
  const paperAccounts = useAsync(() => paperTradingApi.listAccounts({ page: 1, pageSize: 100 }), []);

  const exchangeList = exchanges.data?.items ?? [];
  const allSymbolList = allSymbols.data?.items ?? [];

  const exchangeOptions = useMemo(
    () =>
      exchangeList
        .filter((exchange) => exchange.isActive)
        .map((exchange) => ({
          label: `${exchange.name} (${exchange.code})`,
          value: exchange.id,
        })),
    [exchangeList],
  );

  const allExchangeOptions = useMemo(
    () =>
      exchangeList.map((exchange) => ({
        label: `${exchange.name} (${exchange.code})`,
        value: exchange.id,
      })),
    [exchangeList],
  );

  const symbolOptions = useMemo(
    () =>
      filterSymbolsByExchange(symbols.data?.items ?? [], exchangeId)
        .filter((symbol) => symbol.isActive)
        .map((symbol) => ({
          label: buildSymbolOptionLabel(symbol, exchangeList),
          value: symbol.id,
        })),
    [symbols.data, exchangeId, exchangeList],
  );

  const allSymbolOptions = useMemo(
    () =>
      dedupeSymbolsByName(allSymbolList.filter((symbol) => symbol.isActive)).map((symbol) => ({
        label: buildSymbolOptionLabel(symbol, exchangeList),
        value: symbol.id,
      })),
    [allSymbolList, exchangeList],
  );

  const strategyOptions = useMemo(
    () =>
      enabledStrategiesFirst(strategies.data ?? []).map((strategy) => ({
        label: `${strategy.name}${strategy.isEnabled ? '' : ' (disabled)'}`,
        value: strategy.id,
        disabled: !strategy.isEnabled,
      })),
    [strategies.data],
  );

  function buildStrategyOptions(showDisabled: boolean) {
    const list = showDisabled
      ? strategyOptions
      : strategyOptions.filter((option) => !option.disabled);
    return list;
  }

  const riskProfileOptions = useMemo(
    () =>
      (riskProfiles.data ?? []).map((profile) => ({
        label: profile.isDefault ? `${profile.name} (default)` : profile.name,
        value: profile.id,
      })),
    [riskProfiles.data],
  );

  const paperAccountOptions = useMemo(
    () =>
      (paperAccounts.data?.items ?? [])
        .filter((account) => account.isActive)
        .map((account) => ({
          label: `${account.name} — equity ${account.currentEquity.toLocaleString(undefined, { maximumFractionDigits: 2 })}`,
          value: account.id,
        })),
    [paperAccounts.data],
  );

  const loading =
    exchanges.loading ||
    symbols.loading ||
    allSymbols.loading ||
    strategies.loading ||
    riskProfiles.loading ||
    paperAccounts.loading;

  const error =
    exchanges.error ??
    symbols.error ??
    allSymbols.error ??
    strategies.error ??
    riskProfiles.error ??
    paperAccounts.error;

  function reloadAll() {
    exchanges.reload();
    symbols.reload();
    allSymbols.reload();
    strategies.reload();
    riskProfiles.reload();
    paperAccounts.reload();
  }

  return {
    exchanges: exchangeList,
    symbols: symbols.data?.items ?? [],
    allSymbols: allSymbolList,
    strategies: strategies.data ?? [],
    riskProfiles: riskProfiles.data ?? [],
    paperAccounts: paperAccounts.data?.items ?? [],
    exchangeOptions,
    allExchangeOptions,
    symbolOptions,
    allSymbolOptions,
    strategyOptions,
    buildStrategyOptions,
    riskProfileOptions,
    paperAccountOptions,
    loading,
    error,
    reloadAll,
    reloadExchanges: exchanges.reload,
    reloadSymbols: () => {
      symbols.reload();
      allSymbols.reload();
    },
  };
}
