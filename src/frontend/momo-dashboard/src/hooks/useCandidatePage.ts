import { useCallback, useEffect, useRef, useState } from 'react';
import { parseApiClientError } from '@/utils/apiError';

export interface UseCandidatePageResult<T> {
  data: T | null;
  loading: boolean;
  error: string | null;
  reload: () => void;
}

/**
 * Loads a single page/slice of server data with cancellation and stale-response
 * protection. Whenever `deps` change (route params, filters, pagination, etc.):
 *  - the AbortSignal passed to the previous `fetcher` call is aborted, and
 *  - if the previous request resolves anyway (fetchers are not required to
 *    respect the signal), its result is discarded instead of overwriting state
 *    for the newer request.
 *
 * This is intentionally generic so it can back both page-level candidate tabs
 * (e.g. Validation Lab experiment detail) and grid-level candidate browsers
 * (e.g. Strategy Lab candidate grid) without duplicating the loading logic.
 */
export function useCandidatePage<T>(
  fetcher: (signal: AbortSignal) => Promise<T>,
  deps: unknown[],
): UseCandidatePageResult<T> {
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Keep the latest fetcher without forcing callers to memoize it themselves;
  // reload timing is controlled entirely by `deps`.
  const fetcherRef = useRef(fetcher);
  fetcherRef.current = fetcher;

  const requestIdRef = useRef(0);
  const controllerRef = useRef<AbortController | null>(null);

  const load = useCallback(() => {
    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;
    const requestId = ++requestIdRef.current;

    setLoading(true);
    fetcherRef
      .current(controller.signal)
      .then((result) => {
        if (requestIdRef.current !== requestId) return;
        setData(result);
        setError(null);
        setLoading(false);
      })
      .catch((err) => {
        if (requestIdRef.current !== requestId) return;
        if (controller.signal.aborted) {
          setLoading(false);
          return;
        }
        setError(parseApiClientError(err).message);
        setLoading(false);
      });
    // `deps` is an intentionally dynamic dependency list owned by the caller.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);

  useEffect(() => {
    load();
    return () => {
      controllerRef.current?.abort();
    };
  }, [load]);

  return { data, loading, error, reload: load };
}
