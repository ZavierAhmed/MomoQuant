import type { Exchange, Symbol } from '@/api/domainTypes';
import { buildSymbolOptionLabel } from '@/utils/referenceLookups';

interface SymbolWithExchangeLabelProps {
  symbol?: Symbol | null;
  symbolId?: number | null;
  symbols?: Symbol[];
  exchanges?: Exchange[];
  fallback?: string;
}

export function SymbolWithExchangeLabel({
  symbol,
  symbolId,
  symbols = [],
  exchanges = [],
  fallback = '—',
}: SymbolWithExchangeLabelProps) {
  const resolved =
    symbol ??
    (symbolId ? symbols.find((item) => item.id === symbolId) : undefined);

  if (!resolved) {
    return <span>{fallback}</span>;
  }

  return <span>{buildSymbolOptionLabel(resolved, exchanges)}</span>;
}
