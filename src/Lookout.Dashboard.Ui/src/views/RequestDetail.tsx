import { useState } from 'react';
import { getRequestEntries } from '../api/client';
import type {
  CacheEntryContent,
  DumpEntryContent,
  EfEntryContent,
  EfStackFrame,
  EntryDto,
  ExceptionEntryContent,
  HttpEntryContent,
  LogEntryContent,
  OutboundHttpEntryContent,
  SqlEntryContent,
} from '../api/types';
import { useFetch } from '../api/useFetch';
import { MethodBadge, StatusBadge } from '../components/Badge';
import { formatDuration, formatTimestamp, tryPrettyJson } from '../format';
import { formatSql, sqlPreview } from '../lib/sqlFormatter';
import type { SyntaxToken, TokenType } from '../lib/syntaxHighlight';
import { tokenizeJson, tokenizeSql } from '../lib/syntaxHighlight';
import styles from './RequestDetail.module.css';

export function RequestDetail({ id }: { id: string }) {
  const state = useFetch<EntryDto[]>(`request:${id}`, (signal) => getRequestEntries(id, signal));

  if (state.loading && !state.data) {
    return (
      <div className={styles.root}>
        <a className={styles.back} href="#/">
          ← Back to requests
        </a>
        <div className={styles.caption}>Loading…</div>
      </div>
    );
  }

  if (state.error || !state.data || state.data.length === 0) {
    return (
      <div className={styles.root}>
        <a className={styles.back} href="#/">
          ← Back to requests
        </a>
        <div className={styles.notFound} data-testid="detail-not-found">
          Request not found.
        </div>
      </div>
    );
  }

  return <DetailBody entries={state.data} />;
}

export function DetailBody({ entries }: { entries: EntryDto[] }) {
  const http = entries.find((e) => e.type === 'http');

  if (!http) {
    return (
      <div className={styles.root}>
        <a className={styles.back} href="#/">
          ← Back to requests
        </a>
        <div className={styles.notFound} data-testid="detail-not-found">
          Request not found.
        </div>
      </div>
    );
  }

  const content = http.content as HttpEntryContent;
  const status = content?.statusCode ?? 0;
  const hasDbEntries = entries.some((e) => e.type === 'ef' || e.type === 'sql');
  const httpOutEntries = entries
    .filter((e) => e.type === 'http-out')
    .sort((a, b) => a.timestamp - b.timestamp);
  const cacheEntries = entries
    .filter((e) => e.type === 'cache')
    .sort((a, b) => a.timestamp - b.timestamp);
  const exceptionEntries = entries
    .filter((e) => e.type === 'exception')
    .sort((a, b) => a.timestamp - b.timestamp);
  const logEntries = entries
    .filter((e) => e.type === 'log')
    .sort((a, b) => a.timestamp - b.timestamp);
  const dumpEntries = entries
    .filter((e) => e.type === 'dump')
    .sort((a, b) => a.timestamp - b.timestamp);

  const hasSidePanel =
    hasDbEntries ||
    httpOutEntries.length > 0 ||
    cacheEntries.length > 0 ||
    exceptionEntries.length > 0 ||
    logEntries.length > 0 ||
    dumpEntries.length > 0;

  return (
    <div className={styles.root} data-testid="request-detail">
      <a className={styles.back} href="#/">
        ← Back to requests
      </a>

      <header className={styles.header}>
        <div className={styles.headerRow}>
          <MethodBadge method={content.method} />
          <span className={styles.path}>
            {content.path}
            {content.queryString}
          </span>
          <StatusBadge status={status} />
        </div>
      </header>

      <section className={styles.meta} data-testid="detail-meta">
        <Meta label="Time" value={formatTimestamp(http.timestamp)} />
        <Meta label="Duration" value={formatDuration(content.durationMs)} />
        <Meta label="User" value={content.user ?? '—'} />
        <Meta label="Request ID" value={http.requestId ?? '—'} />
      </section>

      <div className={styles.detailGrid}>
        <div className={`${styles.httpCol} ${!hasSidePanel ? styles.httpColFull : ''}`}>
          <HeadersSection title="Request headers" headers={content.requestHeaders} />
          <BodySection title="Request body" body={content.requestBody} />
          <HeadersSection title="Response headers" headers={content.responseHeaders} />
          <BodySection title="Response body" body={content.responseBody} />
        </div>
        {hasSidePanel && (
          <div className={styles.sideCol}>
            <ExceptionSection entries={exceptionEntries} />
            {hasDbEntries && <DbPanel allEntries={entries} />}
            <OutboundHttpSection entries={httpOutEntries} />
            <CacheSection entries={cacheEntries} />
            <LogSection entries={logEntries} />
            <DumpSection entries={dumpEntries} />
          </div>
        )}
      </div>
    </div>
  );
}

// ─── Token rendering ──────────────────────────────────────────────────────────

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
    <pre className={styles.code}>
      {tokens.map((tok, i) => {
        const cls = TOKEN_CLASS[tok.type];
        return (
          <span key={i} className={cls}>
            {tok.text}
          </span>
        );
      })}
    </pre>
  );
}

// ─── Sub-components ───────────────────────────────────────────────────────────

function Meta({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div className={styles.metaLabel}>{label}</div>
      <div className={styles.metaValue}>{value}</div>
    </div>
  );
}

function HeadersSection({
  title,
  headers,
}: {
  title: string;
  headers: Record<string, string>;
}) {
  const entries = Object.entries(headers ?? {});
  return (
    <details className={styles.section}>
      <summary className={styles.sectionSummary}>
        {title} <span className={styles.caption}>{entries.length}</span>
      </summary>
      <div className={styles.sectionBody}>
        {entries.length === 0 ? (
          <div className={styles.caption}>No headers.</div>
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
          <div className={styles.caption}>Not captured.</div>
        </div>
      </details>
    );
  }

  const pretty = tryPrettyJson(body);
  return (
    <details className={styles.section} open>
      <summary className={styles.sectionSummary}>
        {title} <span className={styles.caption}>{body.length} chars</span>
      </summary>
      <div className={styles.sectionBody}>
        {pretty ? (
          <TokenizedCode tokens={tokenizeJson(pretty)} />
        ) : (
          <pre className={styles.code}>{body}</pre>
        )}
      </div>
    </details>
  );
}

// ─── Exception section ────────────────────────────────────────────────────────

function ExceptionSection({ entries }: { entries: EntryDto[] }) {
  if (entries.length === 0) return null;

  return (
    <details className={styles.section} open data-testid="exception-section">
      <summary className={styles.sectionSummary}>Exception</summary>
      <div className={styles.sectionBody}>
        <ul className={styles.efList}>
          {entries.map((e) => (
            <ExceptionRow key={e.id} entry={e} />
          ))}
        </ul>
      </div>
    </details>
  );
}

function ExceptionRow({ entry }: { entry: EntryDto }) {
  const [open, setOpen] = useState(false);
  const content = entry.content as ExceptionEntryContent;
  const handled = entry.tags['exception.handled'];

  return (
    <li className={styles.efRow} data-testid="exception-row">
      <button
        type="button"
        className={styles.efRowHeader}
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
      >
        <span
          className={`${styles.dbSourceBadge} ${styles.excSourceBadge}`}
          data-testid="exc-source-badge"
        >
          EXC
        </span>
        <span className={`${styles.efPreview} ${styles.exceptionTypeName}`} data-testid="exception-type">
          {content?.exceptionType ?? '—'}
        </span>
        <span className={styles.efMeta}>
          {handled !== undefined ? (
            <span
              className={handled === 'true' ? styles.handledChip : styles.unhandledChip}
              data-testid={handled === 'true' ? 'handled-chip' : 'unhandled-chip'}
            >
              {handled === 'true' ? 'handled' : 'unhandled'}
            </span>
          ) : null}
        </span>
      </button>
      {open ? (
        <div className={styles.efRowBody} data-testid="exception-row-body">
          <div>
            <div className={styles.metaLabel}>Message</div>
            <div className={styles.metaValue}>{content?.message}</div>
          </div>
          {content?.stack && content.stack.length > 0 ? (
            <div data-testid="exception-stack">
              <EfStack stack={content.stack} />
            </div>
          ) : null}
          {content?.innerExceptions && content.innerExceptions.length > 0 ? (
            <div data-testid="inner-exceptions">
              <div className={styles.metaLabel}>Inner exceptions</div>
              <ul className={styles.innerExceptionList}>
                {content.innerExceptions.map((inner, i) => (
                  <li key={i} className={styles.innerExceptionItem}>
                    <span className={styles.exceptionTypeName}>{inner.type}</span>
                    <span className={styles.caption}> — {inner.message}</span>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}
        </div>
      ) : null}
    </li>
  );
}

// ─── Database panel (EF + SQL entries) ───────────────────────────────────────

function DbPanel({ allEntries }: { allEntries: EntryDto[] }) {
  const [dismissed, setDismissed] = useState(false);
  const [highlightN1, setHighlightN1] = useState(false);

  const dbEntries = allEntries
    .filter((e) => e.type === 'ef' || e.type === 'sql')
    .sort((a, b) => a.timestamp - b.timestamp);

  return (
    <>
      {!dismissed && (
        <N1Banner
          dbEntries={dbEntries}
          highlighted={highlightN1}
          onToggleHighlight={() => setHighlightN1((h) => !h)}
          onDismiss={() => setDismissed(true)}
        />
      )}
      <DbSection entries={dbEntries} highlightN1={highlightN1} />
    </>
  );
}

function N1Banner({
  dbEntries,
  highlighted,
  onToggleHighlight,
  onDismiss,
}: {
  dbEntries: EntryDto[];
  highlighted: boolean;
  onToggleHighlight: () => void;
  onDismiss: () => void;
}) {
  const n1Entries = dbEntries.filter((e) => e.tags['n1.group']);
  const groupCount = new Set(n1Entries.map((e) => e.tags['n1.group'])).size;

  if (n1Entries.length === 0) return null;

  const groupText = groupCount === 1 ? '1 group' : `${groupCount} groups`;

  return (
    <div
      className={`${styles.n1Banner} ${highlighted ? styles.n1BannerActive : ''}`}
      data-testid="n1-banner"
      role="alert"
    >
      <button
        type="button"
        className={styles.n1BannerBody}
        onClick={onToggleHighlight}
        aria-pressed={highlighted}
      >
        <span className={styles.n1BannerIcon}>⚠</span>
        <span>
          N+1 detected — {n1Entries.length}{' '}
          {n1Entries.length === 1 ? 'query' : 'queries'} share the same SQL shape ({groupText})
        </span>
      </button>
      <button
        type="button"
        className={styles.n1BannerDismiss}
        onClick={onDismiss}
        aria-label="Dismiss N+1 warning"
      >
        ×
      </button>
    </div>
  );
}

function DbSection({
  entries,
  highlightN1,
}: {
  entries: EntryDto[];
  highlightN1: boolean;
}) {
  if (entries.length === 0) {
    return (
      <details className={styles.section} data-testid="db-section">
        <summary className={styles.sectionSummary}>
          Database queries <span className={styles.caption}>0</span>
        </summary>
        <div className={styles.sectionBody}>
          <div className={styles.caption}>No queries captured for this request.</div>
        </div>
      </details>
    );
  }

  return (
    <details className={styles.section} open data-testid="db-section">
      <summary className={styles.sectionSummary}>
        Database queries <span className={styles.caption}>{entries.length}</span>
      </summary>
      <div className={styles.sectionBody}>
        <ul className={styles.efList}>
          {entries.map((e) => (
            <DbRow
              key={e.id}
              entry={e}
              highlight={highlightN1 && !!e.tags['n1.group']}
            />
          ))}
        </ul>
      </div>
    </details>
  );
}

type DbContent = EfEntryContent | SqlEntryContent;

function DbRow({ entry, highlight }: { entry: EntryDto; highlight: boolean }) {
  const [open, setOpen] = useState(false);
  const content = entry.content as DbContent;
  const rows = content.rowsAffected;
  const source = entry.type === 'ef' ? 'EF' : 'SQL';

  return (
    <li
      className={`${styles.efRow} ${highlight ? styles.n1GroupRow : ''}`}
      data-testid="ef-query-row"
    >
      <button
        type="button"
        className={styles.efRowHeader}
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
      >
        <span
          className={`${styles.dbSourceBadge} ${source === 'EF' ? styles.dbSourceBadgeEf : styles.dbSourceBadgeSql}`}
          data-testid="db-source-badge"
        >
          {source}
        </span>
        <span className={styles.efPreview}>{sqlPreview(content.commandText)}</span>
        <span className={styles.efMeta}>
          <span className={styles.efDuration}>{formatDuration(content.durationMs)}</span>
          {rows !== undefined && rows !== null ? (
            <span className={styles.efRowsBadge}>{rows} rows</span>
          ) : null}
        </span>
      </button>
      {open ? (
        <div className={styles.efRowBody} data-testid="ef-query-body">
          <TokenizedCode tokens={tokenizeSql(formatSql(content.commandText))} />
          <EfParameters parameters={content.parameters} />
          <EfStack stack={content.stack} />
        </div>
      ) : null}
    </li>
  );
}

function EfParameters({ parameters }: { parameters: DbContent['parameters'] }) {
  if (!parameters || parameters.length === 0) {
    return (
      <div>
        <div className={styles.metaLabel}>Parameters</div>
        <div className={styles.caption}>None.</div>
      </div>
    );
  }
  return (
    <div>
      <div className={styles.metaLabel}>Parameters</div>
      <table className={styles.headersTable}>
        <tbody>
          {parameters.map((p, i) => (
            <tr key={`${p.name}-${i}`}>
              <td className={styles.headerName}>{p.name}</td>
              <td className={styles.headerValue}>{p.value ?? <em>null</em>}</td>
              <td className={styles.headerName}>{p.dbType ?? ''}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function EfStack({ stack }: { stack: EfStackFrame[] }) {
  if (!stack || stack.length === 0) {
    return (
      <div>
        <div className={styles.metaLabel}>Stack</div>
        <div className={styles.caption}>No user-code frames captured.</div>
      </div>
    );
  }
  return (
    <div>
      <div className={styles.metaLabel}>Stack</div>
      <ol className={styles.efStack}>
        {stack.map((f, i) => (
          <li key={i} className={styles.efStackFrame}>
            <span className={styles.efStackMethod}>{f.method}</span>
            {f.file ? (
              <span className={styles.efStackLocation}>
                {' '}
                {f.file}
                {f.line != null ? `:${f.line}` : ''}
              </span>
            ) : null}
          </li>
        ))}
      </ol>
    </div>
  );
}

// ─── Outbound HTTP section ────────────────────────────────────────────────────

function OutboundHttpSection({ entries }: { entries: EntryDto[] }) {
  if (entries.length === 0) return null;

  return (
    <details className={styles.section} open data-testid="http-out-section">
      <summary className={styles.sectionSummary}>
        Outbound HTTP <span className={styles.caption}>{entries.length}</span>
      </summary>
      <div className={styles.sectionBody}>
        <ul className={styles.efList}>
          {entries.map((e) => (
            <OutboundHttpRow key={e.id} entry={e} />
          ))}
        </ul>
      </div>
    </details>
  );
}

function OutboundHttpRow({ entry }: { entry: EntryDto }) {
  const [open, setOpen] = useState(false);
  const content = entry.content as OutboundHttpEntryContent;
  const host = entry.tags['http.url.host'] ?? '';
  const urlPath = entry.tags['http.url.path'] ?? '';
  const statusStr = entry.tags['http.status'];
  const status = statusStr ? Number.parseInt(statusStr, 10) : null;
  const hasError = !!entry.tags['http.error'];

  return (
    <li className={styles.efRow} data-testid="http-out-row">
      <button
        type="button"
        className={styles.efRowHeader}
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
      >
        <span
          className={`${styles.dbSourceBadge} ${styles.httpSourceBadge}`}
          data-testid="http-source-badge"
        >
          HTTP
        </span>
        <MethodBadge method={content?.method ?? '—'} />
        <span className={styles.efPreview}>
          {host}
          {urlPath}
        </span>
        <span className={styles.efMeta}>
          {status !== null && Number.isFinite(status) ? <StatusBadge status={status} /> : null}
          {hasError ? (
            <span className={styles.errorBadge} data-testid="http-out-error-badge">
              ERR
            </span>
          ) : null}
          <span className={styles.efDuration}>{formatDuration(entry.durationMs)}</span>
        </span>
      </button>
      {open ? (
        <div className={styles.efRowBody} data-testid="http-out-row-body">
          <div>
            <div className={styles.metaLabel}>URL</div>
            <pre className={styles.code}>{content?.url}</pre>
          </div>
          {content?.errorType ? (
            <div>
              <div className={styles.metaLabel}>Error</div>
              <div className={styles.caption}>
                {content.errorType}: {content.errorMessage}
              </div>
            </div>
          ) : null}
          <HeadersSection title="Request headers" headers={content?.requestHeaders ?? {}} />
          <HeadersSection title="Response headers" headers={content?.responseHeaders ?? {}} />
          {content?.requestBody ? (
            <BodySection title="Request body" body={content.requestBody} />
          ) : null}
          {content?.responseBody ? (
            <BodySection title="Response body" body={content.responseBody} />
          ) : null}
        </div>
      ) : null}
    </li>
  );
}

// ─── Cache section ────────────────────────────────────────────────────────────

function CacheSection({ entries }: { entries: EntryDto[] }) {
  if (entries.length === 0) return null;

  const gets = entries.filter((e) => (e.content as CacheEntryContent)?.operation === 'Get');
  const hits = gets.filter((e) => (e.content as CacheEntryContent)?.hit === true).length;
  const misses = gets.filter((e) => (e.content as CacheEntryContent)?.hit === false).length;
  const total = hits + misses;
  const hitPct = total > 0 ? Math.round((hits / total) * 100) : null;

  return (
    <details className={styles.section} open data-testid="cache-section">
      <summary className={styles.sectionSummary}>
        <span>
          Cache <span className={styles.caption}>{entries.length}</span>
        </span>
        {hitPct !== null ? (
          <span className={styles.hitRatioChip} data-testid="hit-ratio-chip">
            H {hits} / M {misses} ({hitPct}%)
          </span>
        ) : null}
      </summary>
      <div className={styles.sectionBody}>
        <ul className={styles.efList}>
          {entries.map((e) => (
            <CacheRow key={e.id} entry={e} />
          ))}
        </ul>
      </div>
    </details>
  );
}

function CacheRow({ entry }: { entry: EntryDto }) {
  const [open, setOpen] = useState(false);
  const content = entry.content as CacheEntryContent;
  const system = entry.tags['cache.system'];
  const hitTag = entry.tags['cache.hit'];
  const isGet = content?.operation === 'Get';
  const sourceLabel = system === 'memory' ? 'MEM' : 'DIST';

  return (
    <li className={styles.efRow} data-testid="cache-row">
      <button
        type="button"
        className={styles.efRowHeader}
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
      >
        <span
          className={`${styles.dbSourceBadge} ${system === 'memory' ? styles.cacheSourceBadgeMem : styles.cacheSourceBadgeDist}`}
          data-testid="cache-source-badge"
        >
          {sourceLabel}
        </span>
        <span className={styles.cacheOperationBadge} data-testid="cache-operation-badge">
          {content?.operation ?? '—'}
        </span>
        <span className={styles.efPreview}>{entry.tags['cache.key'] ?? content?.key ?? '—'}</span>
        <span className={styles.efMeta}>
          {isGet && hitTag !== undefined ? (
            <span
              className={hitTag === 'true' ? styles.hitDot : styles.missDot}
              data-testid={hitTag === 'true' ? 'cache-hit-dot' : 'cache-miss-dot'}
              title={hitTag === 'true' ? 'Cache hit' : 'Cache miss'}
            />
          ) : null}
          <span className={styles.efDuration}>{formatDuration(entry.durationMs)}</span>
        </span>
      </button>
      {open ? (
        <div className={styles.efRowBody} data-testid="cache-row-body">
          <div>
            <div className={styles.metaLabel}>Key</div>
            <pre className={styles.code}>{content?.key}</pre>
          </div>
          {content?.valueType ? (
            <div>
              <div className={styles.metaLabel}>Value type</div>
              <div className={styles.metaValue}>{content.valueType}</div>
            </div>
          ) : null}
          {content?.valueBytes != null ? (
            <div>
              <div className={styles.metaLabel}>Value bytes</div>
              <div className={styles.metaValue}>{content.valueBytes}</div>
            </div>
          ) : null}
          <div>
            <div className={styles.metaLabel}>System</div>
            <div className={styles.metaValue}>{system ?? '—'}</div>
          </div>
          {entry.tags['cache.provider'] ? (
            <div>
              <div className={styles.metaLabel}>Provider</div>
              <div className={styles.metaValue}>{entry.tags['cache.provider']}</div>
            </div>
          ) : null}
        </div>
      ) : null}
    </li>
  );
}

// ─── Logs section ─────────────────────────────────────────────────────────────

type LogFilter = 'all' | 'warn+' | 'error+';

const LOG_LEVEL_ORDER: Record<string, number> = {
  Trace: 0,
  Debug: 1,
  Information: 2,
  Warning: 3,
  Error: 4,
  Critical: 5,
};

function logLevelClass(level: string): string {
  switch (level) {
    case 'Trace':
      return styles.levelTrace;
    case 'Debug':
      return styles.levelDebug;
    case 'Information':
      return styles.levelInfo;
    case 'Warning':
      return styles.levelWarn;
    case 'Error':
      return styles.levelError;
    case 'Critical':
      return styles.levelCritical;
    default:
      return styles.levelDebug;
  }
}

function abbreviateCategory(category: string): string {
  const parts = category.split('.');
  if (parts.length <= 2) return category;
  return `${parts[0]}…${parts[parts.length - 1]}`;
}

function LogSection({ entries }: { entries: EntryDto[] }) {
  const [filter, setFilter] = useState<LogFilter>('all');

  if (entries.length === 0) return null;

  const filtered =
    filter === 'all'
      ? entries
      : entries.filter((e) => {
          const level = (e.content as LogEntryContent)?.level ?? '';
          const minLevel = filter === 'warn+' ? 3 : 4;
          return (LOG_LEVEL_ORDER[level] ?? 0) >= minLevel;
        });

  return (
    <details className={styles.section} open data-testid="log-section">
      <summary className={styles.sectionSummary}>
        <span>
          Logs <span className={styles.caption}>{entries.length}</span>
        </span>
        <div className={styles.levelFilterChips}>
          {(['all', 'warn+', 'error+'] as LogFilter[]).map((f) => (
            <button
              key={f}
              type="button"
              className={`${styles.levelFilterChip} ${filter === f ? styles.levelFilterChipActive : ''}`}
              onClick={(e) => {
                e.preventDefault();
                e.stopPropagation();
                setFilter(f);
              }}
              data-testid={`log-filter-${f}`}
            >
              {f}
            </button>
          ))}
        </div>
      </summary>
      <div className={styles.sectionBody}>
        {filtered.length === 0 ? (
          <div className={styles.caption}>No entries at this level.</div>
        ) : (
          <ul className={styles.efList}>
            {filtered.map((e) => (
              <LogRow key={e.id} entry={e} />
            ))}
          </ul>
        )}
      </div>
    </details>
  );
}

function LogRow({ entry }: { entry: EntryDto }) {
  const [open, setOpen] = useState(false);
  const content = entry.content as LogEntryContent;
  const level = content?.level ?? '—';

  return (
    <li className={styles.efRow} data-testid="log-row">
      <button
        type="button"
        className={styles.efRowHeader}
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
      >
        <span
          className={`${styles.dbSourceBadge} ${styles.logSourceBadge}`}
          data-testid="log-source-badge"
        >
          LOG
        </span>
        <span
          className={`${styles.levelBadge} ${logLevelClass(level)}`}
          data-testid="log-level-badge"
        >
          {level}
        </span>
        <span className={styles.logCategory} title={content?.category}>
          {abbreviateCategory(content?.category ?? '')}
        </span>
        <span className={styles.efPreview}>{content?.message ?? ''}</span>
      </button>
      {open ? (
        <div className={styles.efRowBody} data-testid="log-row-body">
          <div>
            <div className={styles.metaLabel}>Category</div>
            <div className={styles.metaValue}>{content?.category}</div>
          </div>
          <div>
            <div className={styles.metaLabel}>Message</div>
            <div className={styles.metaValue}>{content?.message}</div>
          </div>
          {content?.eventId ? (
            <div>
              <div className={styles.metaLabel}>Event ID</div>
              <div className={styles.metaValue}>
                {content.eventId.id}
                {content.eventId.name ? ` (${content.eventId.name})` : ''}
              </div>
            </div>
          ) : null}
          {content?.scopes && content.scopes.length > 0 ? (
            <div>
              <div className={styles.metaLabel}>Scopes</div>
              <ol className={styles.efStack}>
                {content.scopes.map((s, i) => (
                  <li key={i} className={styles.efStackFrame}>
                    {s}
                  </li>
                ))}
              </ol>
            </div>
          ) : null}
          {content?.exceptionType ? (
            <div>
              <div className={styles.metaLabel}>Exception</div>
              <div className={styles.metaValue}>
                {content.exceptionType}: {content.exceptionMessage}
              </div>
            </div>
          ) : null}
        </div>
      ) : null}
    </li>
  );
}

// ─── Dump section ─────────────────────────────────────────────────────────────

function callerBasename(filePath: string): string {
  const parts = filePath.replace(/\\/g, '/').split('/');
  return parts[parts.length - 1] ?? filePath;
}

function DumpSection({ entries }: { entries: EntryDto[] }) {
  if (entries.length === 0) return null;

  return (
    <details className={styles.section} open data-testid="dump-section">
      <summary className={styles.sectionSummary}>
        Dump <span className={styles.caption}>{entries.length}</span>
      </summary>
      <div className={styles.sectionBody}>
        <ul className={styles.efList}>
          {entries.map((e) => (
            <DumpRow key={e.id} entry={e} />
          ))}
        </ul>
      </div>
    </details>
  );
}

function DumpRow({ entry }: { entry: EntryDto }) {
  const [open, setOpen] = useState(false);
  const content = entry.content as DumpEntryContent;
  const label = content?.label;
  const fileName = callerBasename(content?.callerFile ?? '');
  const pretty = tryPrettyJson(content?.json);

  return (
    <li className={styles.efRow} data-testid="dump-row">
      <button
        type="button"
        className={styles.efRowHeader}
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
      >
        <span
          className={`${styles.dbSourceBadge} ${styles.dumpSourceBadge}`}
          data-testid="dump-source-badge"
        >
          DUMP
        </span>
        {label ? (
          <span className={styles.dumpLabel} data-testid="dump-label">
            {label}
          </span>
        ) : (
          <span className={`${styles.dumpLabel} ${styles.dumpNoLabel}`} data-testid="dump-no-label">
            (no label)
          </span>
        )}
        <span className={styles.efMeta}>
          <span className={styles.logCategory}>{content?.valueType ?? ''}</span>
          <span className={styles.dumpCallerLocation}>
            {fileName}:{content?.callerLine}
          </span>
        </span>
      </button>
      {open ? (
        <div className={styles.efRowBody} data-testid="dump-row-body">
          {pretty ? (
            <TokenizedCode tokens={tokenizeJson(pretty)} />
          ) : (
            <pre className={styles.code}>{content?.json}</pre>
          )}
          {content?.jsonTruncated ? (
            <div className={styles.dumpTruncationMarker} data-testid="dump-truncation-marker">
              JSON truncated
            </div>
          ) : null}
        </div>
      ) : null}
    </li>
  );
}
