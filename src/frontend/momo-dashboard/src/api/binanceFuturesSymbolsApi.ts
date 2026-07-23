import { apiRequest } from '@/api/apiClient';

export interface DiscoveredBinanceFuturesSymbol {
  rank: number;
  symbol: string;
  baseAsset: string;
  quoteAsset: string;
  contractType: string;
  status: string;
  marginAsset: string;
  pricePrecision: number;
  quantityPrecision: number;
  tickSize: number;
  stepSize: number;
  minQty: number;
  minNotional: number;
  lastPrice: number;
  priceChangePercent24h: number;
  quoteVolume24h: number;
  trades24h: number;
  alreadyAdded: boolean;
}

export interface AddBinanceFuturesSymbolsResult {
  exchangeId: number;
  requestedCount: number;
  addedCount: number;
  skippedCount: number;
  addedSymbols: string[];
  skippedSymbols: string[];
  unknownSymbols: string[];
}

export const binanceFuturesSymbolsApi = {
  discover: (limit = 100) =>
    apiRequest<DiscoveredBinanceFuturesSymbol[]>('/exchanges/binance-futures/discover-symbols', {
      query: { limit },
    }),
  addSymbols: (symbols: string[]) =>
    apiRequest<AddBinanceFuturesSymbolsResult>('/exchanges/binance-futures/add-symbols', {
      method: 'POST',
      body: { symbols },
    }),
};
