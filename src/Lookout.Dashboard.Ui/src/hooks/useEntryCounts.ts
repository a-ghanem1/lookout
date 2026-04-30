import { useEffect, useState } from 'react';
import { getEntryCounts } from '../api/client';
import type { EntryCounts } from '../api/types';

const DEFAULT_COUNTS: EntryCounts = {
  requests: 0,
  queries: 0,
  exceptions: 0,
  logs: 0,
  cache: 0,
  httpClients: 0,
  jobs: 0,
  dump: 0,
};

const POLL_INTERVAL_MS = 2000;

export function useEntryCounts(): EntryCounts {
  const [counts, setCounts] = useState<EntryCounts>(DEFAULT_COUNTS);

  useEffect(() => {
    let cancelled = false;

    const fetchCounts = async () => {
      if (document.visibilityState !== 'visible') return;
      try {
        const data = await getEntryCounts();
        if (!cancelled) setCounts(data);
      } catch {
        // silently ignore — sidebar shows stale / zero counts on error
      }
    };

    void fetchCounts();
    const intervalId = window.setInterval(() => void fetchCounts(), POLL_INTERVAL_MS);

    const onVisibilityChange = () => {
      if (document.visibilityState === 'visible') void fetchCounts();
    };
    document.addEventListener('visibilitychange', onVisibilityChange);

    return () => {
      cancelled = true;
      window.clearInterval(intervalId);
      document.removeEventListener('visibilitychange', onVisibilityChange);
    };
  }, []);

  return counts;
}
