import { ArrowLeft, Eye } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { getEntry, listEntries } from '../../api/client';
import type { EntryDto, ExceptionEntryContent, InnerException } from '../../api/types';
import { EntryListShell } from '../../components/EntryList/EntryListShell';
import { EntryRow } from '../../components/EntryList/EntryRow';
import { formatRelative } from '../../format';
import styles from './ExceptionsPage.module.css';

type HandledFilter = 'all' | 'handled' | 'unhandled';

const HANDLED_LABELS: Record<HandledFilter, string> = {
  all: 'All',
  handled: 'Handled',
  unhandled: 'Unhandled',
};

function getExContent(entry: EntryDto): ExceptionEntryContent {
  return entry.content as ExceptionEntryContent;
}

function truncateTypeName(name: string): string {
  if (name.length <= 60) return name;
  return `${name.slice(0, 28)}…${name.slice(-28)}`;
}

function ExceptionDetail({ id }: { id: string }) {
  const [entry, setEntry] = useState<EntryDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | undefined>();

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(undefined);
    getEntry(id, controller.signal)
      .then((e) => {
        setEntry(e);
        setLoading(false);
      })
      .catch((err: unknown) => {
        if (err instanceof Error && err.name !== 'AbortError') {
          setError(err);
          setLoading(false);
        }
      });
    return () => controller.abort();
  }, [id]);

  if (loading) {
    return (
      <div className={styles.detailWrap}>
        <a href="#/exceptions" className={styles.backLink}><ArrowLeft size={14} strokeWidth={2} />Exceptions</a>
        <span className={styles.detailLoading}>Loading…</span>
      </div>
    );
  }

  if (error || !entry) {
    return (
      <div className={styles.detailWrap}>
        <a href="#/exceptions" className={styles.backLink}><ArrowLeft size={14} strokeWidth={2} />Exceptions</a>
        <p className={styles.detailError}>{error?.message ?? 'Entry not found'}</p>
      </div>
    );
  }

  const content = getExContent(entry);

  return (
    <div className={styles.detailWrap}>
      <div className={styles.detailNavRow}>
        <a href="#/exceptions" className={styles.backLink}><ArrowLeft size={14} strokeWidth={2} />Exceptions</a>
        {entry.requestId && (
          <a
            href={`#/requests/${encodeURIComponent(entry.requestId)}`}
            className={styles.backLink}
          >
            <Eye size={12} strokeWidth={2} />
            Parent request
          </a>
        )}
      </div>
      <div className={styles.detailSection}>
        <span className={`${styles.handledChip} ${content.handled ? styles.handledTrue : styles.handledFalse}`}>
          {content.handled ? 'Handled' : 'Unhandled'}
        </span>
        <code className={styles.exTypeName}>{content.exceptionType}</code>
        <p className={styles.exMessage}>{content.message}</p>
        {content.source && (
          <div className={styles.metaRow}>
            <span className={styles.metaLabel}>Source</span>
            <code className={styles.metaValue}>{content.source}</code>
          </div>
        )}
      </div>
      {content.stack.length > 0 && (
        <div className={styles.detailSection}>
          <div className={styles.sectionTitle}>Stack trace</div>
          <div className={styles.stackList} data-testid="exception-stack">
            {content.stack.map((frame, i) => (
              <div key={i} className={styles.stackFrame}>
                <code className={styles.frameName}>{frame.method}</code>
                {frame.file && (
                  <span className={styles.frameLoc}>
                    {frame.file.split(/[/\\]/).pop()}
                    {frame.line != null ? `:${frame.line}` : ''}
                  </span>
                )}
              </div>
            ))}
          </div>
        </div>
      )}
      {content.innerExceptions.length > 0 && (
        <div className={styles.detailSection}>
          <div className={styles.sectionTitle}>Inner exceptions</div>
          {content.innerExceptions.map((inner: InnerException, i: number) => (
            <div key={i} className={styles.innerException}>
              <code className={styles.exTypeName}>{inner.type}</code>
              <p className={styles.exMessage}>{inner.message}</p>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

export function ExceptionsPage({ id }: { id?: string } = {}) {
  const [handledFilter, setHandledFilter] = useState<HandledFilter>('all');
  const [typeSearch, setTypeSearch] = useState('');
  const [messageSearch, setMessageSearch] = useState('');
  const [debouncedTypeSearch, setDebouncedTypeSearch] = useState('');
  const [debouncedMessageSearch, setDebouncedMessageSearch] = useState('');
  const [entries, setEntries] = useState<EntryDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | undefined>();
  const [retryCount, setRetryCount] = useState(0);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedTypeSearch(typeSearch), 200);
    return () => clearTimeout(timer);
  }, [typeSearch]);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedMessageSearch(messageSearch), 200);
    return () => clearTimeout(timer);
  }, [messageSearch]);

  useEffect(() => {
    if (id) return;
    const controller = new AbortController();
    setLoading(true);
    setError(undefined);

    listEntries({ type: 'exception', limit: 200 }, controller.signal)
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
  }, [id, retryCount]);

  const filtered = useMemo(() => {
    const lowerType = debouncedTypeSearch.toLowerCase();
    const lowerMsg = debouncedMessageSearch.toLowerCase();
    return entries.filter((e) => {
      const content = getExContent(e);
      if (handledFilter === 'handled' && !content.handled) return false;
      if (handledFilter === 'unhandled' && content.handled) return false;
      if (lowerType && !content.exceptionType.toLowerCase().includes(lowerType)) return false;
      if (lowerMsg && !content.message.toLowerCase().includes(lowerMsg)) return false;
      return true;
    });
  }, [entries, handledFilter, debouncedTypeSearch, debouncedMessageSearch]);

  if (id) return <ExceptionDetail id={id} />;

  const filterSlot = (
    <div className={styles.filterBar}>
      <div className={styles.filterGroup} role="group" aria-label="Handled filter">
        {(['all', 'handled', 'unhandled'] as const).map((h) => (
          <button
            key={h}
            className={`${styles.chip} ${handledFilter === h ? styles.chipActive : ''}`}
            onClick={() => setHandledFilter(h)}
            aria-pressed={handledFilter === h}
          >
            {HANDLED_LABELS[h]}
          </button>
        ))}
      </div>
      <input
        type="search"
        placeholder="Search type…"
        className={styles.searchInput}
        value={typeSearch}
        onChange={(e) => setTypeSearch(e.target.value)}
        aria-label="Search exception type"
      />
      <input
        type="search"
        placeholder="Search message…"
        className={styles.searchInput}
        value={messageSearch}
        onChange={(e) => setMessageSearch(e.target.value)}
        aria-label="Search exception message"
      />
    </div>
  );

  return (
    <EntryListShell
      title="Exceptions"
      total={entries.length}
      loading={loading}
      error={error}
      items={filtered}
      estimatedRowHeight={40}
      emptyMessage='No exceptions captured. Throw something to test it: throw new InvalidOperationException("test") from any endpoint.'
      filterSlot={filterSlot}
      onRetry={() => setRetryCount((c) => c + 1)}
      renderRow={(entry) => {
        const content = getExContent(entry);

        return (
          <EntryRow
            timestamp={formatRelative(entry.timestamp)}
            badge={
              <span className={`${styles.handledChip} ${content.handled ? styles.handledTrue : styles.handledFalse}`}>
                {content.handled ? 'Handled' : 'Unhandled'}
              </span>
            }
            summary={
              <span className={styles.summaryRow}>
                <code className={styles.exType}>{truncateTypeName(content.exceptionType)}</code>
                <span className={styles.exMsg}>{content.message}</span>
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
            onClick={() => {
              window.location.hash = `/exceptions/${entry.id}`;
            }}
          />
        );
      }}
    />
  );
}
