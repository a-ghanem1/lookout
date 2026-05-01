import { Eye } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { listEntries } from '../../api/client';
import type { DumpEntryContent, EntryDto } from '../../api/types';
import { EntryListShell } from '../../components/EntryList/EntryListShell';
import { EntryRow } from '../../components/EntryList/EntryRow';
import { ActiveTagsBar } from '../../components/Tags/TagChip';
import { useTagFilter } from '../../hooks/useTagFilter';
import { formatRelative } from '../../format';
import styles from './DumpPage.module.css';

type JsonKind = 'object' | 'array' | 'string' | 'number' | 'boolean' | 'null';

const KIND_LABELS: Record<JsonKind, string> = {
  object: '{ }',
  array: '[ ]',
  string: '" "',
  number: '123',
  boolean: 'T/F',
  null: 'null',
};

const KIND_CSS: Record<JsonKind, string> = {
  object: styles.kindObject,
  array: styles.kindArray,
  string: styles.kindString,
  number: styles.kindNumber,
  boolean: styles.kindBool,
  null: styles.kindNull,
};

function getJsonKind(json: string): JsonKind {
  const t = json.trim();
  if (t.startsWith('{')) return 'object';
  if (t.startsWith('[')) return 'array';
  if (t === 'null') return 'null';
  if (t === 'true' || t === 'false') return 'boolean';
  if (t.startsWith('"')) return 'string';
  return 'number';
}

function prettyJson(json: string): string {
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}

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
  const { activeTags, removeTag, clear: clearTags } = useTagFilter();
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

    listEntries({ type: 'dump', tags: activeTags.length > 0 ? activeTags : undefined, limit: 200 }, controller.signal)
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
  }, [activeTags, retryCount]);

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
      <ActiveTagsBar tags={activeTags} onRemove={removeTag} onClear={clearTags} />
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
        const kind = getJsonKind(content.json);

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
                  <span className={`${styles.kindBadge} ${KIND_CSS[kind]}`} data-testid="dump-kind-badge">
                    {KIND_LABELS[kind]}
                  </span>
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
                <div className={styles.expandMeta}>
                  <span className={styles.typeLabel}>{content.valueType}</span>
                  {content.jsonTruncated && (
                    <span className={styles.truncatedNote}>truncated</span>
                  )}
                </div>
                <pre className={styles.jsonBlock}>{prettyJson(content.json)}</pre>
              </div>
            )}
          </div>
        );
      }}
    />
  );
}
