import { useEffect, useRef, useState } from 'react';

export interface FetchState<T> {
  data: T | undefined;
  error: Error | undefined;
  loading: boolean;
  reload: () => void;
}

/**
 * Minimal data-fetching hook. No caching, no stale-while-revalidate. Cancels
 * in-flight requests when the key changes or the component unmounts.
 *
 * `poll` re-runs on an interval while `idle` is true. The caller decides idle
 * (e.g. list view sets idle when no filter input is focused).
 */
export function useFetch<T>(
  key: string,
  fetcher: (signal: AbortSignal) => Promise<T>,
  options: { poll?: number; idle?: boolean } = {},
): FetchState<T> {
  const [data, setData] = useState<T | undefined>(undefined);
  const [error, setError] = useState<Error | undefined>(undefined);
  const [loading, setLoading] = useState<boolean>(true);
  const [tick, setTick] = useState(0);
  const fetcherRef = useRef(fetcher);
  fetcherRef.current = fetcher;

  useEffect(() => {
    const controller = new AbortController();
    let cancelled = false;
    setLoading(true);
    fetcherRef
      .current(controller.signal)
      .then((value) => {
        if (cancelled) return;
        setData(value);
        setError(undefined);
      })
      .catch((err: unknown) => {
        if (cancelled || controller.signal.aborted) return;
        setError(err instanceof Error ? err : new Error(String(err)));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
      controller.abort();
    };
  }, [key, tick]);

  useEffect(() => {
    if (!options.poll || !options.idle) return;
    const id = window.setInterval(() => setTick((t) => t + 1), options.poll);
    return () => window.clearInterval(id);
  }, [options.poll, options.idle]);

  return {
    data,
    error,
    loading,
    reload: () => setTick((t) => t + 1),
  };
}
