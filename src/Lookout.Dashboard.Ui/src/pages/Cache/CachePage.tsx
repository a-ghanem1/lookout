import { Eye } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { getCacheSummary, listEntries } from '../../api/client';
import type { CacheEntryContent, CacheSummary, EntryDto } from '../../api/types';
import { EntryListShell } from '../../components/EntryList/EntryListShell';
import { EntryRow } from '../../components/EntryList/EntryRow';
import { formatDuration, formatRelative } from '../../format';
import styles from './CachePage.module.css';

type Kind = 'all' | 'mem' | 'dist';
type Outcome = 'all' | 'hit' | 'miss' | 'set' | 'remove';

const KIND_LABELS: Record<Kind, string> = { all: 'All', mem: 'MEM', dist: 'DIST' };
const OUTCOME_LABELS: Record<Outcome, string> = {
  all: 'All', hit: 'Hit', miss: 'Miss', set: 'Set', remove: 'Remove',
};

function getCacheContent(entry: EntryDto): CacheEntryContent {
  return entry.content as CacheEntryContent;
}

function getOutcomeLabel(content: CacheEntryContent): string {
  if (content.operation === 'Get') return content.hit === true ? 'Hit' : content.hit === false ? 'Miss' : '–';
  return '–';
}

function matchesKind(entry: EntryDto, kind: Kind): boolean {
  if (kind === 'all') return true;
  const system = entry.tags['cache.system'] ?? '';
  return kind === 'mem' ? system === 'memory' : system === 'distributed';
}

function matchesOutcome(content: CacheEntryContent, outcome: Outcome): boolean {
  if (outcome === 'all') return true;
  if (outcome === 'hit') return content.operation === 'Get' && content.hit === true;
  if (outcome === 'miss') return content.operation === 'Get' && content.hit === false;
  if (outcome === 'set') return content.operation === 'Set';
  if (outcome === 'remove') return content.operation === 'Remove';
  return true;
}

function truncateKey(key: string): string {
  if (key.length <= 60) return key;
  return `${key.slice(0, 28)}…${key.slice(-28)}`;
}

function truncateType(type: string): string {
  if (type.length <= 60) return type;
  return `${type.slice(0, 57)}…`;
}

export function CachePage({ id: _id }: { id?: string } = {}) {
  const [kind, setKind] = useState<Kind>('all');
  const [outcome, setOutcome] = useState<Outcome>('all');
  const [keySearch, setKeySearch] = useState('');
  const [debouncedKeySearch, setDebouncedKeySearch] = useState('');
  const [entries, setEntries] = useState<EntryDto[]>([]);
  const [summary, setSummary] = useState<CacheSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | undefined>();
  const [retryCount, setRetryCount] = useState(0);
  const [expandedIds, setExpandedIds] = useState<Set<string>>(new Set());

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedKeySearch(keySearch), 200);
    return () => clearTimeout(timer);
  }, [keySearch]);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(undefined);

    Promise.all([
      listEntries({ type: 'cache', q: debouncedKeySearch || undefined, limit: 200 }, controller.signal),
      getCacheSummary(controller.signal),
    ])
      .then(([resp, sum]) => {
        setEntries(resp.entries);
        setSummary(sum);
        setLoading(false);
      })
      .catch((err: unknown) => {
        if (err instanceof Error && err.name !== 'AbortError') {
          setError(err);
          setLoading(false);
        }
      });

    return () => controller.abort();
  }, [debouncedKeySearch, retryCount]);

  function toggleExpand(id: string) {
    setExpandedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  const filtered = useMemo(
    () =>
      entries.filter((e) => {
        if (!matchesKind(e, kind)) return false;
        return matchesOutcome(getCacheContent(e), outcome);
      }),
    [entries, kind, outcome],
  );

  const totalGets = summary ? summary.hits + summary.misses : 0;
  const hitPct = totalGets > 0 ? Math.round((summary!.hits / totalGets) * 100) : 0;

  const filterSlot = (
    <div className={styles.filterBarWrap}>
      {summary !== null && (
        <div className={styles.hitRatioBar} data-testid="cache-hit-ratio">
          <span className={styles.hitCount}>Hits {summary.hits.toLocaleString()}</span>
          {' / '}
          <span className={styles.missCount}>Misses {summary.misses.toLocaleString()}</span>
          {totalGets > 0 && <span className={styles.hitPct}> ({hitPct}%)</span>}
        </div>
      )}
      <div className={styles.filterBar}>
        <div className={styles.filterGroup} role="group" aria-label="Kind filter">
          {(['all', 'mem', 'dist'] as const).map((k) => (
            <button
              key={k}
              className={`${styles.chip} ${kind === k ? styles.chipActive : ''}`}
              onClick={() => setKind(k)}
              aria-pressed={kind === k}
            >
              {KIND_LABELS[k]}
            </button>
          ))}
        </div>
        <div className={styles.filterGroup} role="group" aria-label="Outcome filter">
          {(['all', 'hit', 'miss', 'set', 'remove'] as const).map((o) => (
            <button
              key={o}
              className={`${styles.chip} ${outcome === o ? styles.chipActive : ''}`}
              onClick={() => setOutcome(o)}
              aria-pressed={outcome === o}
            >
              {OUTCOME_LABELS[o]}
            </button>
          ))}
        </div>
        <input
          type="search"
          placeholder="Search key…"
          className={styles.searchInput}
          value={keySearch}
          onChange={(e) => setKeySearch(e.target.value)}
          aria-label="Search cache key"
        />
      </div>
    </div>
  );

  return (
    <EntryListShell
      title="Cache"
      total={entries.length}
      loading={loading}
      error={error}
      items={filtered}
      estimatedRowHeight={40}
      emptyMessage="No cache events captured. Use IMemoryCache or IDistributedCache to start capturing."
      filterSlot={filterSlot}
      onRetry={() => setRetryCount((c) => c + 1)}
      renderRow={(entry) => {
        const content = getCacheContent(entry);
        const system = entry.tags['cache.system'] ?? '';
        const kindLabel = system === 'memory' ? 'MEM' : system === 'distributed' ? 'DIST' : '?';
        const outcomeLabel = getOutcomeLabel(content);
        const isExpanded = expandedIds.has(entry.id);

        return (
          <div>
            <EntryRow
              timestamp={formatRelative(entry.timestamp)}
              badge={
                <span className={styles.badgeRow}>
                  <span
                    className={`${styles.kindBadge} ${system === 'memory' ? styles.kindMem : styles.kindDist}`}
                  >
                    {kindLabel}
                  </span>
                  <span className={styles.opBadge}>{content.operation}</span>
                </span>
              }
              summary={
                <span className={styles.keyRow}>
                  {outcomeLabel !== '–' && (
                    <span
                      className={`${styles.outcomePill} ${outcomeLabel === 'Hit' ? styles.outcomeHit : styles.outcomeMiss}`}
                    >
                      {outcomeLabel}
                    </span>
                  )}
                  <span className={styles.keyPreview}>{truncateKey(content.key)}</span>
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
                    <Eye size={12} strokeWidth={2} />
                  </a>
                ) : (
                  <span className={styles.background}>Background</span>
                )
              }
              onClick={() => toggleExpand(entry.id)}
            />
            {isExpanded && (
              <div className={styles.expandPanel} data-testid={`expand-${entry.id}`}>
                <div className={styles.expandRow}>
                  <span className={styles.expandLabel}>Key</span>
                  <code className={styles.fullKey}>{content.key}</code>
                </div>
                {content.valueType && (
                  <div className={styles.expandRow}>
                    <span className={styles.expandLabel}>Type</span>
                    <code className={styles.expandValue} title={content.valueType}>
                      {truncateType(content.valueType)}
                    </code>
                  </div>
                )}
                {content.valueBytes != null && (
                  <div className={styles.expandRow}>
                    <span className={styles.expandLabel}>Size</span>
                    <span className={styles.expandValue}>
                      {content.valueBytes.toLocaleString()} bytes
                    </span>
                  </div>
                )}
              </div>
            )}
          </div>
        );
      }}
    />
  );
}
