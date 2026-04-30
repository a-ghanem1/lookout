import { ArrowUpRight } from 'lucide-react';
import { useEffect, useState } from 'react';
import { listEntries } from '../../api/client';
import type { EntryDto } from '../../api/types';
import { EntryListShell } from '../../components/EntryList/EntryListShell';
import { EntryRow } from '../../components/EntryList/EntryRow';
import { formatDuration, formatRelative } from '../../format';
import styles from './HttpClientsPage.module.css';

type StatusClass = 'all' | '2xx' | '3xx' | '4xx' | '5xx' | 'errors';

const STATUS_CLASS_LABELS: Record<StatusClass, string> = {
  all: 'All',
  '2xx': '2xx',
  '3xx': '3xx',
  '4xx': '4xx',
  '5xx': '5xx',
  errors: 'Errors',
};

const HTTP_METHODS = ['All', 'GET', 'POST', 'PUT', 'PATCH', 'DELETE'];

function statusClassToParam(sc: StatusClass): string | undefined {
  if (sc === 'all' || sc === 'errors') return undefined;
  const base = parseInt(sc[0], 10) * 100;
  return `${base}-${base + 99}`;
}

function getStatusCode(entry: EntryDto): number | null {
  const tag = entry.tags['http.status'];
  if (!tag) return null;
  const n = parseInt(tag, 10);
  return isNaN(n) ? null : n;
}

function entryHasError(entry: EntryDto): boolean {
  return 'http.error' in entry.tags;
}

function statusColorClass(code: number | null, hasError: boolean): string {
  if (hasError || code == null) return styles.statusError;
  if (code >= 200 && code < 300) return styles.statusSuccess;
  if (code >= 300 && code < 400) return styles.statusNeutral;
  if (code >= 400 && code < 500) return styles.statusWarn;
  return styles.statusError;
}

export function HttpClientsPage({ id: _id }: { id?: string } = {}) {
  const [statusClass, setStatusClass] = useState<StatusClass>('all');
  const [method, setMethod] = useState('All');
  const [host, setHost] = useState('');
  const [debouncedHost, setDebouncedHost] = useState('');
  const [entries, setEntries] = useState<EntryDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | undefined>();
  const [retryCount, setRetryCount] = useState(0);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedHost(host), 200);
    return () => clearTimeout(timer);
  }, [host]);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(undefined);

    listEntries(
      {
        type: 'http-out',
        status: statusClassToParam(statusClass),
        method: method !== 'All' ? method : undefined,
        host: debouncedHost || undefined,
        errorsOnly: statusClass === 'errors' ? true : undefined,
        limit: 200,
      },
      controller.signal,
    )
      .then((resp) => {
        setEntries(resp.entries);
        setLoading(false);
      })
      .catch((err: unknown) => {
        if (err instanceof Error && err.name !== 'AbortError') {
          setError(err);
          setLoading(false);
        }
      });

    return () => controller.abort();
  }, [statusClass, method, debouncedHost, retryCount]);

  const filterSlot = (
    <div className={styles.filterBar}>
      <div className={styles.filterGroup} role="group" aria-label="Status class filter">
        {(['all', '2xx', '3xx', '4xx', '5xx', 'errors'] as const).map((sc) => (
          <button
            key={sc}
            className={`${styles.chip} ${statusClass === sc ? styles.chipActive : ''}`}
            onClick={() => setStatusClass(sc)}
            aria-pressed={statusClass === sc}
          >
            {STATUS_CLASS_LABELS[sc]}
          </button>
        ))}
      </div>
      <select
        className={styles.methodSelect}
        value={method}
        onChange={(e) => setMethod(e.target.value)}
        aria-label="Method filter"
      >
        {HTTP_METHODS.map((m) => (
          <option key={m} value={m}>
            {m}
          </option>
        ))}
      </select>
      <input
        type="search"
        placeholder="Filter by host…"
        className={styles.searchInput}
        value={host}
        onChange={(e) => setHost(e.target.value)}
        aria-label="Filter by host"
      />
    </div>
  );

  return (
    <EntryListShell
      title="HTTP clients"
      total={entries.length}
      loading={loading}
      error={error}
      items={entries}
      estimatedRowHeight={40}
      emptyMessage="No outbound HTTP calls captured. Use a typed or named HttpClient to start capturing."
      filterSlot={filterSlot}
      onRetry={() => setRetryCount((c) => c + 1)}
      renderRow={(entry) => {
        const code = getStatusCode(entry);
        const hasErr = entryHasError(entry);
        const urlHost = entry.tags['http.url.host'] ?? '';
        const urlPath = entry.tags['http.url.path'] ?? '';
        const httpMethod = entry.tags['http.method'] ?? '';

        return (
          <EntryRow
            timestamp={formatRelative(entry.timestamp)}
            badge={<span className={styles.methodBadge}>{httpMethod}</span>}
            summary={
              <span className={styles.urlPreview}>
                <span className={statusColorClass(code, hasErr)}>
                  {hasErr && code == null ? (
                    <span className={styles.errPill}>ERR</span>
                  ) : (
                    code ?? '—'
                  )}
                </span>
                {' '}
                <span className={styles.urlHost}>{urlHost}</span>
                <span className={styles.urlPath}>{urlPath}</span>
              </span>
            }
            duration={<span className={styles.duration}>{formatDuration(entry.durationMs)}</span>}
            requestLink={
              entry.requestId ? (
                <a
                  href={`#/requests/${encodeURIComponent(entry.requestId)}`}
                  className={styles.reqLink}
                  onClick={(e) => e.stopPropagation()}
                  aria-label="View parent request"
                >
                  <ArrowUpRight size={14} strokeWidth={2} />
                </a>
              ) : (
                <span className={styles.background}>Background</span>
              )
            }
            onClick={() => {
              window.location.hash = `#/http-clients/${encodeURIComponent(entry.id)}`;
            }}
          />
        );
      }}
    />
  );
}
