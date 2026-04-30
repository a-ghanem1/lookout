import { useCallback, useEffect, useRef, useState } from 'react';
import { searchEntries } from '../../api/client';
import type { SearchResultDto } from '../../api/types';
import { formatTimestamp } from '../../format';
import styles from './SearchPalette.module.css';

const SECTION_ORDER = [
  'http', 'ef', 'sql', 'exception', 'log', 'cache', 'http-out', 'job-enqueue', 'job-execution', 'dump',
];

const SECTION_LABELS: Record<string, string> = {
  http: 'Requests',
  ef: 'Queries',
  sql: 'Queries',
  exception: 'Exceptions',
  log: 'Logs',
  cache: 'Cache',
  'http-out': 'HTTP Clients',
  'job-enqueue': 'Jobs',
  'job-execution': 'Jobs',
  dump: 'Dump',
};

const TYPE_ROUTE: Record<string, string> = {
  http: 'requests',
  ef: 'queries',
  sql: 'queries',
  exception: 'exceptions',
  log: 'logs',
  cache: 'cache',
  'http-out': 'http-clients',
  'job-enqueue': 'jobs',
  'job-execution': 'jobs',
  dump: 'dump',
};

interface GroupedResults {
  label: string;
  type: string;
  results: SearchResultDto[];
}

function groupResults(results: SearchResultDto[]): GroupedResults[] {
  const map = new Map<string, SearchResultDto[]>();
  for (const r of results) {
    const key = r.type === 'sql' ? 'ef' : r.type;
    if (!map.has(key)) map.set(key, []);
    map.get(key)!.push(r);
  }

  const groups: GroupedResults[] = [];
  for (const type of SECTION_ORDER) {
    const items = map.get(type);
    if (items && items.length > 0) {
      groups.push({
        label: SECTION_LABELS[type] ?? type,
        type,
        results: items,
      });
    }
  }
  return groups;
}

function routeForResult(r: SearchResultDto): string {
  const section = TYPE_ROUTE[r.type] ?? 'requests';
  if (r.type === 'http' && r.requestId) return `#/requests/${encodeURIComponent(r.requestId)}`;
  if (section === 'queries') return `#/queries/${encodeURIComponent(r.id)}`;
  if (section === 'exceptions') return `#/exceptions/${encodeURIComponent(r.id)}`;
  if (section === 'http-clients') return `#/http-clients/${encodeURIComponent(r.id)}`;
  if (section === 'jobs') return `#/jobs/${encodeURIComponent(r.id)}`;
  return `#/${section}`;
}

/** Parse §...§ highlight markers without dangerouslySetInnerHTML. */
function parseSnippet(snippet: string): { text: string; highlight: boolean }[] {
  const parts = snippet.split('§');
  return parts.map((text, i) => ({ text, highlight: i % 2 === 1 }));
}

interface SearchPaletteProps {
  open: boolean;
  onClose: () => void;
}

export function SearchPalette({ open, onClose }: SearchPaletteProps) {
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<SearchResultDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [selectedIndex, setSelectedIndex] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const abortRef = useRef<AbortController | null>(null);

  const groups = groupResults(results);
  const flatResults = groups.flatMap((g) => g.results.slice(0, 5));

  // Focus input when opened
  useEffect(() => {
    if (open) {
      setQuery('');
      setResults([]);
      setSelectedIndex(0);
      setTimeout(() => inputRef.current?.focus(), 0);
    }
  }, [open]);

  // Debounced search
  useEffect(() => {
    if (!query.trim()) {
      setResults([]);
      return;
    }
    const timer = setTimeout(() => {
      abortRef.current?.abort();
      abortRef.current = new AbortController();
      setLoading(true);

      searchEntries(query, 50, abortRef.current.signal)
        .then((r) => {
          setResults(r);
          setSelectedIndex(0);
        })
        .catch(() => {})
        .finally(() => setLoading(false));
    }, 150);
    return () => clearTimeout(timer);
  }, [query]);

  const navigate = useCallback(
    (result: SearchResultDto) => {
      window.location.hash = routeForResult(result);
      onClose();
    },
    [onClose],
  );

  // Keyboard navigation
  useEffect(() => {
    if (!open) return;
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose();
      } else if (e.key === 'ArrowDown') {
        e.preventDefault();
        setSelectedIndex((i) => Math.min(i + 1, flatResults.length - 1));
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        setSelectedIndex((i) => Math.max(i - 1, 0));
      } else if (e.key === 'Enter') {
        e.preventDefault();
        const r = flatResults[selectedIndex];
        if (r) navigate(r);
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [open, flatResults, selectedIndex, navigate, onClose]);

  if (!open) return null;

  let globalIdx = 0;

  return (
    <div className={styles.backdrop} onClick={onClose} data-testid="search-palette-backdrop">
      <div
        className={styles.palette}
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-label="Search"
        aria-modal="true"
      >
        <div className={styles.inputRow}>
          <input
            ref={inputRef}
            className={styles.input}
            type="text"
            placeholder="Search across requests, queries, logs, exceptions, jobs, cache, dump…"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            data-testid="search-input"
            aria-label="Search"
            autoComplete="off"
          />
          <div className={styles.syntaxHint} title="Supported: foo bar = AND, foo OR bar, foo* = prefix, NOT foo">
            ?
          </div>
        </div>

        <div className={styles.results}>
          {!query.trim() && (
            <div className={styles.empty}>
              Type to search across requests, queries, logs, exceptions, jobs, cache, dump.
            </div>
          )}
          {query.trim() && !loading && results.length === 0 && (
            <div className={styles.empty}>
              No matches for <strong>{query}</strong>. Try a partial word — FTS supports <code>*</code> prefix matching.
            </div>
          )}
          {groups.map((group) => {
            const visible = group.results.slice(0, 5);
            const more = group.results.length - 5;
            return (
              <div key={group.type} className={styles.group}>
                <div className={styles.groupLabel}>{group.label}</div>
                {visible.map((r) => {
                  const idx = globalIdx++;
                  const selected = idx === selectedIndex;
                  return (
                    <button
                      key={r.id}
                      type="button"
                      className={`${styles.resultRow} ${selected ? styles.resultRowSelected : ''}`}
                      onClick={() => navigate(r)}
                      data-testid="search-result-row"
                    >
                      <span className={styles.resultType}>{SECTION_LABELS[r.type] ?? r.type}</span>
                      <span className={styles.resultSnippet}>
                        {parseSnippet(r.snippet).map((part, i) =>
                          part.highlight ? (
                            <mark key={i} className={styles.highlight}>{part.text}</mark>
                          ) : (
                            <span key={i}>{part.text}</span>
                          ),
                        )}
                      </span>
                      <span className={styles.resultTime}>{formatTimestamp(r.timestamp)}</span>
                    </button>
                  );
                })}
                {more > 0 && (
                  <div className={styles.seeAll}>
                    +{more} more in {group.label}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
