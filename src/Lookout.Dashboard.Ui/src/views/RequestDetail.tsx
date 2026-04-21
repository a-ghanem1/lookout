import { getEntry } from '../api/client';
import type { EntryDto, HttpEntryContent } from '../api/types';
import { useFetch } from '../api/useFetch';
import { MethodBadge, StatusBadge } from '../components/Badge';
import { formatDuration, formatTimestamp, tryPrettyJson } from '../format';
import styles from './RequestDetail.module.css';

export function RequestDetail({ id }: { id: string }) {
  const state = useFetch<EntryDto>(`entry:${id}`, (signal) => getEntry(id, signal));

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

  if (state.error || !state.data) {
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

  return <DetailBody entry={state.data} />;
}

export function DetailBody({ entry }: { entry: EntryDto }) {
  const content = entry.content as HttpEntryContent;
  const status = content?.statusCode ?? 0;

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
        <Meta label="Time" value={formatTimestamp(entry.timestamp)} />
        <Meta label="Duration" value={formatDuration(content.durationMs)} />
        <Meta label="User" value={content.user ?? '—'} />
        <Meta label="Request ID" value={entry.requestId ?? '—'} />
      </section>

      <HeadersSection title="Request headers" headers={content.requestHeaders} />
      <BodySection title="Request body" body={content.requestBody} />
      <HeadersSection title="Response headers" headers={content.responseHeaders} />
      <BodySection title="Response body" body={content.responseBody} />
    </div>
  );
}

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
        <pre className={styles.code}>{pretty ?? body}</pre>
      </div>
    </details>
  );
}
