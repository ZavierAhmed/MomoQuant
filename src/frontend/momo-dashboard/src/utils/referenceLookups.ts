import type { Exchange, PaperAccount, RiskProfile, Strategy, Symbol } from '@/api/domainTypes';

export function findExchange(exchanges: Exchange[], id?: number | null): Exchange | undefined {
  if (!id) return undefined;
  return exchanges.find((exchange) => exchange.id === id);
}

export function findSymbol(symbols: Symbol[], id?: number | null): Symbol | undefined {
  if (!id) return undefined;
  return symbols.find((symbol) => symbol.id === id);
}

export function findStrategy(strategies: Strategy[], id?: number | null): Strategy | undefined {
  if (!id) return undefined;
  return strategies.find((strategy) => strategy.id === id);
}

export function findRiskProfile(profiles: RiskProfile[], id?: number | null): RiskProfile | undefined {
  if (!id) return undefined;
  return profiles.find((profile) => profile.id === id);
}

export function findPaperAccount(accounts: PaperAccount[], id?: number | null): PaperAccount | undefined {
  if (!id) return undefined;
  return accounts.find((account) => account.id === id);
}

export function exchangeLabel(exchanges: Exchange[], id?: number | null): string {
  const exchange = findExchange(exchanges, id);
  return exchange ? exchange.name : '—';
}

export function exchangeCodeLabel(exchanges: Exchange[], id?: number | null): string {
  const exchange = findExchange(exchanges, id);
  return exchange ? exchange.code : '—';
}

export function symbolWithExchangeLabel(
  symbol: Symbol | undefined | null,
  exchanges?: Exchange[],
): string {
  if (!symbol) return '—';
  const exchangeName =
    symbol.exchangeName ??
    (exchanges ? findExchange(exchanges, symbol.exchangeId)?.name : undefined) ??
    symbol.exchangeCode ??
    (exchanges ? findExchange(exchanges, symbol.exchangeId)?.code : undefined);
  return exchangeName ? `${symbol.symbol} — ${exchangeName}` : symbol.symbol;
}

export function symbolLabel(
  symbols: Symbol[],
  id?: number | null,
  exchanges?: Exchange[],
): string {
  return symbolWithExchangeLabel(findSymbol(symbols, id), exchanges);
}

export function strategyLabel(strategies: Strategy[], id?: number | null): string {
  return findStrategy(strategies, id)?.name ?? '—';
}

export function riskProfileLabel(profiles: RiskProfile[], id?: number | null): string {
  return findRiskProfile(profiles, id)?.name ?? '—';
}

export function paperAccountLabel(accounts: PaperAccount[], id?: number | null): string {
  return findPaperAccount(accounts, id)?.name ?? '—';
}

export function buildSymbolOptionLabel(symbol: Symbol, exchanges?: Exchange[]): string {
  return symbolWithExchangeLabel(symbol, exchanges);
}

export function filterSymbolsByExchange(symbols: Symbol[], exchangeId?: number | null): Symbol[] {
  if (!exchangeId) return [];
  return dedupeSymbolsByName(symbols.filter((symbol) => symbol.exchangeId === exchangeId));
}

export function dedupeSymbolsByName(symbols: Symbol[]): Symbol[] {
  const seen = new Set<string>();
  const deduped: Symbol[] = [];
  for (const symbol of symbols) {
    const key = `${symbol.exchangeId}:${symbol.symbol.trim().toUpperCase()}`;
    if (seen.has(key)) continue;
    seen.add(key);
    deduped.push(symbol);
  }
  return deduped;
}

export function enabledStrategiesFirst(strategies: Strategy[]): Strategy[] {
  return [...strategies].sort((left, right) => Number(right.isEnabled) - Number(left.isEnabled));
}
