import { Eye } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { listEntries } from '../../api/client';
import type { EntryDto, LogEntryContent } from '../../api/types';
import { EntryListShell } from '../../components/EntryList/EntryListShell';
import { EntryRow } from '../../components/EntryList/EntryRow';
import { formatRelative } from '../../format';
import styles from './LogsPage.module.css';

type LevelFilter = 'all' | 'warn+' | 'error+';

const LEVEL_FILTER_LABELS: Record<LevelFilter, string> = {
  all: 'All',
  'warn+': 'Warn+',
  'error+': 'Error+',
};

function getLogContent(entry: EntryDto): LogEntryContent {
  return entry.content as LogEntryContent;
}

function levelOrdinal(level: string): number {
  switch (level) {
    case 'Trace': return 0;
    case 'Debug': return 1;
    case 'Information': return 2;
    case 'Warning': return 3;
    case 'Error': return 4;
    case 'Critical': return 5;
    default: return -1;
  }
}

function matchesLevel(level: string, filter: LevelFilter): boolean {
  if (filter === 'all') return true;
  const ord = levelOrdinal(level);
  if (filter === 'warn+') return ord >= 3;
  if (filter === 'error+') return ord >= 4;
  return true;
}

function levelChipStyle(level: string): string {
  switch (level) {
    case 'Information': return styles.levelInfo;
    case 'Warning': return styles.levelWarn;
    case 'Error':
    case 'Critical': return styles.levelDanger;
    default: return styles.levelNeutral;
  }
}

function truncateCategory(category: string): string {
  if (category.length <= 40) return category;
  const dot = category.lastIndexOf('.');
  if (dot > 0 && category.length - dot <= 32) return `…${category.slice(dot)}`;
  return `…${category.slice(-38)}`;
}

export function LogsPage() {
  const [levelFilter, setLevelFilter] = useState<LevelFilter>('all');
  const [categorySearch, setCategorySearch] = useState('');
  const [messageSearch, setMessageSearch] = useState('');
  const [debouncedCategorySearch, setDebouncedCategorySearch] = useState('');
  const [debouncedMessageSearch, setDebouncedMessageSearch] = useState('');
  const [entries, setEntries] = useState<EntryDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | undefined>();
  const [retryCount, setRetryCount] = useState(0);
  const [expandedIds, setExpandedIds] = useState<Set<string>>(new Set());

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedCategorySearch(categorySearch), 200);
    return () => clearTimeout(timer);
  }, [categorySearch]);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedMessageSearch(messageSearch), 200);
    return () => clearTimeout(timer);
  }, [messageSearch]);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(undefined);

    listEntries({ type: 'log', limit: 200 }, controller.signal)
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
  }, [retryCount]);

  function toggleExpand(id: string) {
    setExpandedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  const filtered = useMemo(() => {
    const lowerCat = debouncedCategorySearch.toLowerCase();
    const lowerMsg = debouncedMessageSearch.toLowerCase();
    return entries.filter((e) => {
      const content = getLogContent(e);
      if (!matchesLevel(content.level, levelFilter)) return false;
      if (lowerCat && !content.category.toLowerCase().includes(lowerCat)) return false;
      if (lowerMsg && !content.message.toLowerCase().includes(lowerMsg)) return false;
      return true;
    });
  }, [entries, levelFilter, debouncedCategorySearch, debouncedMessageSearch]);

  const filterSlot = (
    <div className={styles.filterBar}>
      <div className={styles.filterGroup} role="group" aria-label="Level filter">
        {(['all', 'warn+', 'error+'] as const).map((l) => (
          <button
            key={l}
            className={`${styles.chip} ${levelFilter === l ? styles.chipActive : ''}`}
            onClick={() => setLevelFilter(l)}
            aria-pressed={levelFilter === l}
          >
            {LEVEL_FILTER_LABELS[l]}
          </button>
        ))}
      </div>
      <input
        type="search"
        placeholder="Search category…"
        className={styles.searchInput}
        value={categorySearch}
        onChange={(e) => setCategorySearch(e.target.value)}
        aria-label="Search category"
      />
      <input
        type="search"
        placeholder="Search message…"
        className={styles.searchInput}
        value={messageSearch}
        onChange={(e) => setMessageSearch(e.target.value)}
        aria-label="Search message"
      />
    </div>
  );

  return (
    <EntryListShell
      title="Logs"
      total={entries.length}
      loading={loading}
      error={error}
      items={filtered}
      estimatedRowHeight={40}
      emptyMessage="No logs captured for the current retention window."
      filterSlot={filterSlot}
      onRetry={() => setRetryCount((c) => c + 1)}
      renderRow={(entry) => {
        const content = getLogContent(entry);
        const isExpanded = expandedIds.has(entry.id);

        return (
          <div>
            <EntryRow
              timestamp={formatRelative(entry.timestamp)}
              badge={
                <span className={`${styles.levelChip} ${levelChipStyle(content.level)}`}>
                  {content.level}
                </span>
              }
              summary={
                <span className={styles.summaryRow}>
                  <code className={styles.category}>{truncateCategory(content.category)}</code>
                  <span className={styles.msgPreview}>{content.message}</span>
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
                <p className={styles.fullMessage}>{content.message}</p>
                {content.scopes.length > 0 && (
                  <div className={styles.expandRow}>
                    <span className={styles.expandLabel}>Scopes</span>
                    <span className={styles.scopeChain}>
                      {content.scopes.join(' › ')}
                    </span>
                  </div>
                )}
                {content.eventId && content.eventId.id !== 0 && (
                  <div className={styles.expandRow}>
                    <span className={styles.expandLabel}>Event</span>
                    <code className={styles.expandValue}>
                      {content.eventId.id}
                      {content.eventId.name ? ` (${content.eventId.name})` : ''}
                    </code>
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
