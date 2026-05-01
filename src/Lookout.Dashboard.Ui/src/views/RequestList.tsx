import { useMemo, useState } from 'react';
import { listEntries } from '../api/client';
import type { EntryDto, HttpEntryContent } from '../api/types';
import { useFetch } from '../api/useFetch';
import { MethodBadge, StatusBadge } from '../components/Badge';
import { ActiveTagsBar } from '../components/Tags/TagChip';
import { useTagFilter } from '../hooks/useTagFilter';
import { formatDuration, formatTimestamp } from '../format';
import styles from './RequestList.module.css';

const NOISE_KEY = 'lookout:show-noise';

function readShowNoise(): boolean {
  try { return localStorage.getItem(NOISE_KEY) === 'true'; } catch { return false; }
}

function isNoise(entry: EntryDto): boolean {
  const content = entry.content as HttpEntryContent | undefined;
  const status = content?.statusCode ?? 0;
  const duration = entry.durationMs ?? 0;
  const dbCount = Number.parseInt(entry.tags['db.count'] ?? '0', 10);
  const logCount = Number.parseInt(entry.tags['log.count'] ?? '0', 10);
  const cacheCount = Number.parseInt(entry.tags['cache.count'] ?? '0', 10);
  const httpOutCount = Number.parseInt(entry.tags['http.out.count'] ?? '0', 10);
  const hasException = entry.tags['exception'] === 'true';
  return status === 404 && duration < 1 && dbCount === 0 && logCount === 0 && cacheCount === 0 && httpOutCount === 0 && !hasException;
}

interface Filters {
  method: string;
  status: string;
  path: string;
}

const EMPTY: Filters = { method: '', status: '', path: '' };

export function RequestList() {
  const { activeTags, removeTag, clear: clearTags } = useTagFilter();
  const [filters, setFilters] = useState<Filters>(EMPTY);
  const [focused, setFocused] = useState(false);
  const [showNoise, setShowNoise] = useState(readShowNoise);

  const key = useMemo(
    () => JSON.stringify({ t: 'http', ...filters, tags: activeTags }),
    [filters, activeTags],
  );

  const state = useFetch(
    key,
    (signal) =>
      listEntries(
        {
          type: 'http',
          method: filters.method || undefined,
          status: filters.status || undefined,
          path: filters.path || undefined,
          tags: activeTags.length > 0 ? activeTags : undefined,
          limit: 100,
        },
        signal,
      ),
    { poll: 2000, idle: !focused },
  );

  const allEntries = state.data?.entries ?? [];

  const entries = useMemo(
    () => (showNoise ? allEntries : allEntries.filter((e) => !isNoise(e))),
    [allEntries, showNoise],
  );

  const hideUser = entries.every((e) => {
    const c = e.content as HttpEntryContent | undefined;
    return !c?.user && !e.tags['http.user'];
  });

  function toggleNoise() {
    setShowNoise((v) => {
      const next = !v;
      try { localStorage.setItem(NOISE_KEY, String(next)); } catch { /* ignore */ }
      return next;
    });
  }

  return (
    <div className={styles.root} data-testid="request-list">
      <section className={styles.filters} aria-label="filters">
        <label className={styles.filter}>
          <span className={styles.label}>Method</span>
          <select
            className={styles.select}
            value={filters.method}
            onChange={(e) => setFilters((f) => ({ ...f, method: e.target.value }))}
            onFocus={() => setFocused(true)}
            onBlur={() => setFocused(false)}
          >
            <option value="">Any</option>
            <option value="GET">GET</option>
            <option value="POST">POST</option>
            <option value="PUT">PUT</option>
            <option value="PATCH">PATCH</option>
            <option value="DELETE">DELETE</option>
          </select>
        </label>
        <label className={styles.filter}>
          <span className={styles.label}>Status</span>
          <input
            type="text"
            className={styles.input}
            placeholder="200 or 500-599"
            value={filters.status}
            onChange={(e) => setFilters((f) => ({ ...f, status: e.target.value }))}
            onFocus={() => setFocused(true)}
            onBlur={() => setFocused(false)}
          />
        </label>
        <label className={styles.filter}>
          <span className={styles.label}>Path</span>
          <input
            type="text"
            className={styles.input}
            placeholder="/api/…"
            value={filters.path}
            onChange={(e) => setFilters((f) => ({ ...f, path: e.target.value }))}
            onFocus={() => setFocused(true)}
            onBlur={() => setFocused(false)}
          />
        </label>
        <button
          type="button"
          className={`${styles.noiseToggle} ${showNoise ? styles.noiseToggleActive : ''}`}
          onClick={toggleNoise}
          aria-pressed={showNoise}
          data-testid="show-noise-toggle"
        >
          {showNoise ? 'Hide noise' : 'Show noise'}
        </button>
      </section>

      <ActiveTagsBar tags={activeTags} onRemove={removeTag} onClear={clearTags} />

      {state.error ? (
        <div className={styles.error} role="alert">
          {state.error.message}
        </div>
      ) : null}

      {allEntries.length > 0 && (
        <div className={styles.summary} data-testid="request-summary">
          Showing {entries.length} of {allEntries.length} requests
        </div>
      )}

      <RequestTable entries={entries} loading={state.loading} hideUser={hideUser} />
    </div>
  );
}

function RequestTable({ entries, loading, hideUser }: { entries: EntryDto[]; loading: boolean; hideUser: boolean }) {
  if (!loading && entries.length === 0) {
    return (
      <div className={styles.tableWrap}>
        <div className={styles.empty} data-testid="empty-state">
          <div className={styles.emptyTitle}>No requests captured yet</div>
          <div className={styles.emptyHint}>
            Hit an endpoint in your app — the request will appear here.
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.tableWrap}>
      <table className={styles.table} data-testid="request-table">
        <thead>
          <tr>
            <th className={styles.th}>Time</th>
            <th className={styles.th}>Method</th>
            <th className={styles.th}>Path</th>
            <th className={styles.th}>Status</th>
            <th className={styles.th}>Duration</th>
            {!hideUser && <th className={styles.th}>User</th>}
            <th className={styles.th}>Signals</th>
          </tr>
        </thead>
        <tbody>
          {entries.map((entry) => (
            <RequestRow key={entry.id} entry={entry} hideUser={hideUser} />
          ))}
        </tbody>
      </table>
    </div>
  );
}

function RequestRow({ entry, hideUser }: { entry: EntryDto; hideUser: boolean }) {
  const content = entry.content as HttpEntryContent | undefined;
  const method = entry.tags['http.method'] ?? content?.method ?? '—';
  const path = entry.tags['http.path'] ?? content?.path ?? '—';
  const statusStr = entry.tags['http.status'] ?? String(content?.statusCode ?? '');
  const status = Number.parseInt(statusStr, 10);
  const user = entry.tags['http.user'] ?? content?.user ?? '';
  const dbCountStr = entry.tags['db.count'];
  const dbCount = dbCountStr !== undefined ? Number.parseInt(dbCountStr, 10) : null;
  const n1Detected = entry.tags['n1.detected'] === 'true';
  const httpOutCountStr = entry.tags['http.out.count'];
  const httpOutCount = httpOutCountStr !== undefined ? Number.parseInt(httpOutCountStr, 10) : null;
  const cacheCountStr = entry.tags['cache.count'];
  const cacheCount = cacheCountStr !== undefined ? Number.parseInt(cacheCountStr, 10) : null;
  const hasException = entry.tags['exception'] === 'true';
  const maxLogLevel = entry.tags['log.maxLevel'];
  const logCountStr = entry.tags['log.count'];
  const logCount = logCountStr !== undefined ? Number.parseInt(logCountStr, 10) : null;
  const dumpCountStr = entry.tags['dump.count'];
  const dumpCount = dumpCountStr !== undefined ? Number.parseInt(dumpCountStr, 10) : null;
  const jobCountStr = entry.tags['job.enqueue.count'];
  const jobCount = jobCountStr !== undefined ? Number.parseInt(jobCountStr, 10) : null;

  const go = () => {
    const target = entry.requestId ?? entry.id;
    window.location.hash = `#/requests/${encodeURIComponent(target)}`;
  };

  return (
    <tr
      className={styles.row}
      onClick={go}
      tabIndex={0}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          go();
        }
      }}
      data-testid="request-row"
    >
      <td className={`${styles.td} ${styles.mono}`}>{formatTimestamp(entry.timestamp)}</td>
      <td className={styles.td}>
        <MethodBadge method={method} />
      </td>
      <td className={`${styles.td} ${styles.path}`}>{path}</td>
      <td className={styles.td}>
        {Number.isFinite(status) ? <StatusBadge status={status} /> : '—'}
      </td>
      <td className={`${styles.td} ${styles.duration}`}>{formatDuration(entry.durationMs)}</td>
      {!hideUser && <td className={`${styles.td} ${styles.mono}`}>{user || '—'}</td>}
      <td className={styles.td}>
        <div className={styles.dbCell}>
          {dbCount !== null && dbCount > 0 ? (
            <span className={styles.dbCountBadge} data-testid="db-count-badge">
              db: {dbCount}
            </span>
          ) : null}
          {n1Detected ? (
            <span className={styles.n1Badge} data-testid="n1-badge">
              N+1
            </span>
          ) : null}
          {httpOutCount !== null && httpOutCount > 0 ? (
            <span className={styles.httpOutCountBadge} data-testid="http-out-count-badge">
              http: {httpOutCount}
            </span>
          ) : null}
          {cacheCount !== null && cacheCount > 0 ? (
            <span className={styles.cacheCountBadge} data-testid="cache-count-badge">
              cache: {cacheCount}
            </span>
          ) : null}
          {hasException ? (
            <span className={styles.errorTagBadge} data-testid="exception-badge">
              error
            </span>
          ) : null}
          {maxLogLevel === 'Error' || maxLogLevel === 'Critical' ? (
            <span className={styles.errorTagBadge} data-testid="log-error-badge">
              log: {maxLogLevel.toLowerCase()}
            </span>
          ) : maxLogLevel === 'Warning' ? (
            <span className={styles.warnTagBadge} data-testid="log-warn-badge">
              log: warn
            </span>
          ) : null}
          {logCount !== null && logCount > 0 ? (
            <span className={styles.logCountBadge} data-testid="log-count-badge">
              log: {logCount}
            </span>
          ) : null}
          {dumpCount !== null && dumpCount > 0 ? (
            <span className={styles.dumpCountBadge} data-testid="dump-count-badge">
              dump: {dumpCount}
            </span>
          ) : null}
          {jobCount !== null && jobCount > 0 ? (
            <span className={styles.jobCountBadge} data-testid="job-count-badge">
              jobs: {jobCount}
            </span>
          ) : null}
        </div>
      </td>
    </tr>
  );
}
