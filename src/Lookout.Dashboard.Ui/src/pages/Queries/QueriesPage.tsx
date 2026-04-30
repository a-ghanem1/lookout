import { ArrowUpRight } from 'lucide-react';
import { useEffect, useState } from 'react';
import { listEntries } from '../../api/client';
import type { EntryDto } from '../../api/types';
import { EntryListShell } from '../../components/EntryList/EntryListShell';
import { EntryRow } from '../../components/EntryList/EntryRow';
import { formatDuration, formatRelative } from '../../format';
import styles from './QueriesPage.module.css';

type Source = 'all' | 'ef' | 'sql';
type DurationFilter = 0 | 10 | 100 | 1000;

const SOURCE_LABELS: Record<Source, string> = { all: 'All', ef: 'EF', sql: 'SQL' };
const DURATION_LABELS: Record<DurationFilter, string> = {
  0: 'All',
  10: '>10ms',
  100: '>100ms',
  1000: '>1s',
};

function getSqlPreview(entry: EntryDto): string {
  const c = entry.content as Record<string, unknown>;
  const sql = typeof c?.commandText === 'string' ? c.commandText : '';
  return sql.length > 80 ? `${sql.slice(0, 77)}…` : sql;
}

function durationColorClass(ms: number | undefined | null): string {
  if (!ms || ms < 50) return styles.durationNeutral;
  if (ms < 500) return styles.durationWarn;
  return styles.durationError;
}

export function QueriesPage({ id: _id }: { id?: string } = {}) {
  const [source, setSource] = useState<Source>('all');
  const [minDuration, setMinDuration] = useState<DurationFilter>(0);
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [entries, setEntries] = useState<EntryDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | undefined>();
  const [retryCount, setRetryCount] = useState(0);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search), 200);
    return () => clearTimeout(timer);
  }, [search]);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(undefined);

    listEntries(
      {
        type: source === 'all' ? 'ef,sql' : source,
        sort: 'duration',
        minDurationMs: minDuration > 0 ? minDuration : undefined,
        q: debouncedSearch || undefined,
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
  }, [source, minDuration, debouncedSearch, retryCount]);

  const filterSlot = (
    <div className={styles.filterBar}>
      <div className={styles.filterGroup} role="group" aria-label="Source filter">
        {(['all', 'ef', 'sql'] as const).map((s) => (
          <button
            key={s}
            className={`${styles.chip} ${source === s ? styles.chipActive : ''}`}
            onClick={() => setSource(s)}
            aria-pressed={source === s}
          >
            {SOURCE_LABELS[s]}
          </button>
        ))}
      </div>
      <div className={styles.filterGroup} role="group" aria-label="Duration filter">
        {([0, 10, 100, 1000] as const).map((d) => (
          <button
            key={d}
            className={`${styles.chip} ${minDuration === d ? styles.chipActive : ''}`}
            onClick={() => setMinDuration(d)}
            aria-pressed={minDuration === d}
          >
            {DURATION_LABELS[d]}
          </button>
        ))}
      </div>
      <input
        type="search"
        placeholder="Search SQL…"
        className={styles.searchInput}
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        aria-label="Search SQL"
      />
    </div>
  );

  return (
    <EntryListShell
      title="Queries"
      total={entries.length}
      loading={loading}
      error={error}
      items={entries}
      estimatedRowHeight={40}
      emptyMessage="No database queries captured yet. Run an EF Core or ADO.NET query to start capturing."
      filterSlot={filterSlot}
      onRetry={() => setRetryCount((c) => c + 1)}
      renderRow={(entry) => (
        <EntryRow
          timestamp={formatRelative(entry.timestamp)}
          badge={
            <span
              className={`${styles.sourceBadge} ${entry.type === 'ef' ? styles.badgeEf : styles.badgeSql}`}
            >
              {entry.type === 'ef' ? 'EF' : 'SQL'}
            </span>
          }
          summary={<span className={styles.sqlPreview}>{getSqlPreview(entry)}</span>}
          duration={
            <span className={durationColorClass(entry.durationMs)}>
              {formatDuration(entry.durationMs)}
            </span>
          }
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
            window.location.hash = `#/queries/${encodeURIComponent(entry.id)}`;
          }}
        />
      )}
    />
  );
}
