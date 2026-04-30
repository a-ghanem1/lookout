import { useEffect, useMemo, useState } from 'react';
import { listEntries } from '../../api/client';
import type { DumpEntryContent, EntryDto } from '../../api/types';
import { EntryListShell } from '../../components/EntryList/EntryListShell';
import { EntryRow } from '../../components/EntryList/EntryRow';
import { formatRelative } from '../../format';
import styles from './DumpPage.module.css';

function getDumpContent(entry: EntryDto): DumpEntryContent {
  return entry.content as DumpEntryContent;
}

function getJsonPreview(json: string): string {
  return json.length > 80 ? `${json.slice(0, 77)}…` : json;
}

function getCallerDisplay(content: DumpEntryContent): string {
  const parts = content.callerFile.split(/[/\\]/);
  const file = parts[parts.length - 1] ?? content.callerFile;
  return `${file}:${content.callerLine}`;
}

export function DumpPage({ id: _id }: { id?: string } = {}) {
  const [labelSearch, setLabelSearch] = useState('');
  const [callerSearch, setCallerSearch] = useState('');
  const [debouncedLabelSearch, setDebouncedLabelSearch] = useState('');
  const [debouncedCallerSearch, setDebouncedCallerSearch] = useState('');
  const [entries, setEntries] = useState<EntryDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | undefined>();
  const [retryCount, setRetryCount] = useState(0);
  const [expandedIds, setExpandedIds] = useState<Set<string>>(new Set());

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedLabelSearch(labelSearch), 200);
    return () => clearTimeout(timer);
  }, [labelSearch]);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedCallerSearch(callerSearch), 200);
    return () => clearTimeout(timer);
  }, [callerSearch]);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(undefined);

    listEntries({ type: 'dump', limit: 200 }, controller.signal)
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
    const lowerLabel = debouncedLabelSearch.toLowerCase();
    const lowerCaller = debouncedCallerSearch.toLowerCase();
    return entries.filter((e) => {
      const content = getDumpContent(e);
      if (lowerLabel && !(content.label ?? '').toLowerCase().includes(lowerLabel)) return false;
      if (lowerCaller && !content.callerFile.toLowerCase().includes(lowerCaller)) return false;
      return true;
    });
  }, [entries, debouncedLabelSearch, debouncedCallerSearch]);

  const filterSlot = (
    <div className={styles.filterBar}>
      <input
        type="search"
        placeholder="Search label…"
        className={styles.searchInput}
        value={labelSearch}
        onChange={(e) => setLabelSearch(e.target.value)}
        aria-label="Search label"
      />
      <input
        type="search"
        placeholder="Search caller file…"
        className={styles.searchInput}
        value={callerSearch}
        onChange={(e) => setCallerSearch(e.target.value)}
        aria-label="Search caller file"
      />
    </div>
  );

  return (
    <EntryListShell
      title="Dump"
      total={entries.length}
      loading={loading}
      error={error}
      items={filtered}
      estimatedRowHeight={40}
      emptyMessage='No Lookout.Dump() calls captured yet. Add Lookout.Dump(obj, "label") anywhere in your request path to start capturing.'
      filterSlot={filterSlot}
      onRetry={() => setRetryCount((c) => c + 1)}
      renderRow={(entry) => {
        const content = getDumpContent(entry);
        const label = content.label ?? '(unnamed)';
        const isUnnamed = !content.label;
        const isExpanded = expandedIds.has(entry.id);
        const callerDisplay = getCallerDisplay(content);

        return (
          <div>
            <EntryRow
              timestamp={formatRelative(entry.timestamp)}
              badge={
                <span className={`${styles.labelBadge} ${isUnnamed ? styles.labelUnnamed : ''}`}>
                  {label}
                </span>
              }
              summary={
                <span className={styles.summaryRow}>
                  <span className={styles.caller}>{callerDisplay}</span>
                  <span className={styles.jsonPreview}>{getJsonPreview(content.json)}</span>
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
                    &#8599;
                  </a>
                ) : (
                  <span className={styles.background}>Background</span>
                )
              }
              onClick={() => toggleExpand(entry.id)}
            />
            {isExpanded && (
              <div className={styles.expandPanel} data-testid={`expand-${entry.id}`}>
                <pre className={styles.jsonBlock}>{content.json}</pre>
              </div>
            )}
          </div>
        );
      }}
    />
  );
}
