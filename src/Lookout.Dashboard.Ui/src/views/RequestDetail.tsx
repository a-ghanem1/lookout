import { useState } from 'react';
import { getRequestEntries } from '../api/client';
import type {
  EfEntryContent,
  EfStackFrame,
  EntryDto,
  HttpEntryContent,
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
        <div className={`${styles.httpCol} ${!hasDbEntries ? styles.httpColFull : ''}`}>
          <HeadersSection title="Request headers" headers={content.requestHeaders} />
          <BodySection title="Request body" body={content.requestBody} />
          <HeadersSection title="Response headers" headers={content.responseHeaders} />
          <BodySection title="Response body" body={content.responseBody} />
        </div>
        {hasDbEntries && (
          <div className={styles.sideCol}>
            <DbPanel allEntries={entries} />
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
