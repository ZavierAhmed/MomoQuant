import { useCallback, useEffect, useRef, useState } from 'react';

interface AsyncState<T> {
  data: T | null;
  error: string | null;
  loading: boolean;
  reload: () => void;
  reloadSilent: () => void;
}

export function useAsync<T>(loader: () => Promise<T>, deps: unknown[] = []): AsyncState<T> {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [tick, setTick] = useState(0);
  const silentRef = useRef(false);

  const reload = useCallback(() => {
    silentRef.current = false;
    setTick((value) => value + 1);
  }, []);

  const reloadSilent = useCallback(() => {
    silentRef.current = true;
    setTick((value) => value + 1);
  }, []);

  useEffect(() => {
    let active = true;
    const silent = silentRef.current;
    silentRef.current = false;

    if (!silent) {
      setLoading(true);
      setError(null);
    }

    loader()
      .then((result) => {
        if (active) {
          setData(result);
        }
      })
      .catch((err: unknown) => {
        if (active) {
          setError(err instanceof Error ? err.message : 'Request failed.');
          if (!silent) {
            setData(null);
          }
        }
      })
      .finally(() => {
        if (active) {
          setLoading(false);
        }
      });

    return () => {
      active = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tick, ...deps]);

  return { data, error, loading, reload, reloadSilent };
}
