export function resolvePriceDecimals(price: number): number {
  const abs = Math.abs(price);
  if (abs >= 100) return 2;
  if (abs >= 1) return 4;
  if (abs >= 0.01) return 6;
  return 8;
}

export function formatPrice(value: number | null | undefined, decimals = 2): string {
  if (value == null || Number.isNaN(value)) {
    return '—';
  }
  const safeDecimals = Math.min(Math.max(decimals, 0), 8);
  return value.toLocaleString('en-US', {
    minimumFractionDigits: safeDecimals,
    maximumFractionDigits: safeDecimals,
  });
}

export function formatPriceRange(
  low: number | null | undefined,
  high: number | null | undefined,
  decimals = 2,
): string {
  return `${formatPrice(low, decimals)} – ${formatPrice(high, decimals)}`;
}
