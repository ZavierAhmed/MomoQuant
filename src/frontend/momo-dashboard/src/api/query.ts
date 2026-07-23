export type QueryParams = Record<string, string | number | boolean | undefined | null>;

export function buildQuery(params?: object): string {
  if (!params) {
    return '';
  }

  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.set(key, String(value));
    }
  }

  const query = search.toString();
  return query ? `?${query}` : '';
}
