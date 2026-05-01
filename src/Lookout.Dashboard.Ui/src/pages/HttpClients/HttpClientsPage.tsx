import { ArrowLeft, Eye } from 'lucide-react';
import { useEffect, useState } from 'react';
import { getEntry, listEntries } from '../../api/client';
import type { EntryDto, OutboundHttpEntryContent } from '../../api/types';
import { EntryListShell } from '../../components/EntryList/EntryListShell';
import { EntryRow } from '../../components/EntryList/EntryRow';
import { ActiveTagsBar } from '../../components/Tags/TagChip';
import { useTagFilter } from '../../hooks/useTagFilter';
import { formatDuration, formatRelative, tryPrettyJson } from '../../format';
import { tokenizeJson } from '../../lib/syntaxHighlight';
import type { SyntaxToken, TokenType } from '../../lib/syntaxHighlight';
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

// ─── Shared detail sub-components ────────────────────────────────────────────

const TOKEN_CLASS: Record<TokenType, string | undefined> = {
  keyword: styles.tokenKeyword,
  key: styles.tokenKey,
  string: styles.tokenString,
  number: styles.tokenNumber,
  bool: styles.tokenBool,
  null: styles.tokenNull,
  punct: styles.tokenPunct,
  plain: undefined,
};

function TokenizedCode({ tokens }: { tokens: SyntaxToken[] }) {
  return (
    <pre className={styles.codeBlock}>
      {tokens.map((tok, i) => {
        const cls = TOKEN_CLASS[tok.type];
        return cls ? <span key={i} className={cls}>{tok.text}</span> : tok.text;
      })}
    </pre>
  );
}

function HeadersSection({ title, headers }: { title: string; headers: Record<string, string> | undefined | null }) {
  const entries = Object.entries(headers ?? {});
  return (
    <details className={styles.section}>
      <summary className={styles.sectionSummary}>
        {title} <span className={styles.sectionCount}>{entries.length}</span>
      </summary>
      <div className={styles.sectionBody}>
        {entries.length === 0 ? (
          <span className={styles.empty}>No headers.</span>
        ) : (
          <table className={styles.headersTable}>
            <tbody>
              {entries.map(([name, value]) => (
                <tr key={name}>
                  <td className={styles.headerName}>{name}</td>
                  <td className={styles.headerValue}>{value}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </details>
  );
}

function BodySection({ title, body }: { title: string; body: string | undefined | null }) {
  if (!body) {
    return (
      <details className={styles.section}>
        <summary className={styles.sectionSummary}>{title}</summary>
        <div className={styles.sectionBody}>
          <span className={styles.empty}>Not captured.</span>
        </div>
      </details>
    );
  }
  const pretty = tryPrettyJson(body);
  return (
    <details className={styles.section} open>
      <summary className={styles.sectionSummary}>
        {title} <span className={styles.sectionCount}>{body.length} chars</span>
      </summary>
      <div className={styles.sectionBody}>
        {pretty ? (
          <TokenizedCode tokens={tokenizeJson(pretty)} />
        ) : (
          <pre className={styles.codeBlock}>{body}</pre>
        )}
      </div>
    </details>
  );
}

// ─── Detail view ──────────────────────────────────────────────────────────────

function HttpClientDetail({ id }: { id: string }) {
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
        <a href="#/http-clients" className={styles.backLink}>
          <ArrowLeft size={14} strokeWidth={2} />HTTP clients
        </a>
        <span className={styles.detailLoading}>Loading…</span>
      </div>
    );
  }

  if (error || !entry) {
    return (
      <div className={styles.detailWrap}>
        <a href="#/http-clients" className={styles.backLink}>
          <ArrowLeft size={14} strokeWidth={2} />HTTP clients
        </a>
        <p className={styles.detailError}>{error?.message ?? 'Entry not found'}</p>
      </div>
    );
  }

  const content = entry.content as OutboundHttpEntryContent;
  const statusCode = entry.tags['http.status'] ? parseInt(entry.tags['http.status'], 10) : content.statusCode;
  const hasError = !!entry.tags['http.error'] || !!content.errorType;

  return (
    <div className={styles.detailWrap}>
      <div className={styles.detailNavRow}>
        <a href="#/http-clients" className={styles.backLink}>
          <ArrowLeft size={14} strokeWidth={2} />HTTP clients
        </a>
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
        <div className={styles.detailHeaderRow}>
          <span className={styles.methodBadge}>{content.method}</span>
          <span className={`${statusColorClass(statusCode ?? null, hasError)} ${styles.detailStatus}`}>
            {hasError && statusCode == null ? (
              <span className={styles.errPill}>ERR</span>
            ) : (
              statusCode ?? '—'
            )}
          </span>
          <span className={styles.detailDuration}>{formatDuration(entry.durationMs)}</span>
        </div>
        <pre className={styles.detailUrl}>{content.url}</pre>

        {content.errorType && (
          <div className={styles.errorDetail}>
            <span className={styles.errorType}>{content.errorType}</span>
            {content.errorMessage && (
              <span className={styles.errorMessage}>{content.errorMessage}</span>
            )}
          </div>
        )}
      </div>

      <HeadersSection title="Request headers" headers={content.requestHeaders} />
      <BodySection title="Request body" body={content.requestBody} />
      <HeadersSection title="Response headers" headers={content.responseHeaders} />
      <BodySection title="Response body" body={content.responseBody} />
    </div>
  );
}

// ─── List view ────────────────────────────────────────────────────────────────

export function HttpClientsPage({ id }: { id?: string } = {}) {
  const { activeTags, removeTag, clear: clearTags } = useTagFilter();
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
    if (id) return;
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
        tags: activeTags.length > 0 ? activeTags : undefined,
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
  }, [id, statusClass, method, debouncedHost, activeTags, retryCount]);

  if (id) return <HttpClientDetail id={id} />;

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
      <ActiveTagsBar tags={activeTags} onRemove={removeTag} onClear={clearTags} />
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
                  <Eye size={12} strokeWidth={2} />
                </a>
              ) : (
                <span className={styles.background}>Background</span>
              )
            }
            onClick={() => {
              window.location.hash = `/http-clients/${encodeURIComponent(entry.id)}`;
            }}
          />
        );
      }}
    />
  );
}
