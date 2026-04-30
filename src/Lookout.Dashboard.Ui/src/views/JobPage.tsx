import { getEntry } from '../api/client';
import type { EntryDto, JobEnqueueEntryContent, JobExecutionEntryContent } from '../api/types';
import { useFetch } from '../api/useFetch';
import { formatDuration, formatTimestamp } from '../format';
import styles from './JobPage.module.css';

export function JobPage({ id }: { id: string }) {
  const state = useFetch<EntryDto>(`job:${id}`, (signal) => getEntry(id, signal));

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
        <div className={styles.notFound} data-testid="job-not-found">
          Job entry not found.
        </div>
      </div>
    );
  }

  return <JobBody entry={state.data} />;
}

export function JobBody({ entry }: { entry: EntryDto }) {
  if (entry.type === 'job-enqueue') {
    return <JobEnqueueBody entry={entry} />;
  }
  if (entry.type === 'job-execution') {
    return <JobExecutionBody entry={entry} />;
  }

  return (
    <div className={styles.root}>
      <a className={styles.back} href="#/">
        ← Back to requests
      </a>
      <div className={styles.notFound} data-testid="job-not-found">
        Job entry not found.
      </div>
    </div>
  );
}

function JobEnqueueBody({ entry }: { entry: EntryDto }) {
  const content = entry.content as JobEnqueueEntryContent;

  return (
    <div className={styles.root} data-testid="job-enqueue-detail">
      <a className={styles.back} href="#/">
        ← Back to requests
      </a>
      <header className={styles.header}>
        <span className={`${styles.typeBadge} ${styles.typeBadgeEnq}`}>ENQ</span>
        <span className={styles.title} data-testid="job-title">
          {shortTypeName(content?.jobType)}.{content?.methodName ?? '—'}
        </span>
      </header>

      <section className={styles.meta} data-testid="job-meta">
        <MetaField label="Time" value={formatTimestamp(entry.timestamp)} />
        <MetaField label="Duration" value={formatDuration(entry.durationMs)} />
        <MetaField label="Job ID" value={content?.jobId ?? '—'} />
        <MetaField label="Queue" value={content?.queue ?? '—'} />
        <MetaField label="State" value={content?.state ?? '—'} />
        {entry.requestId ? (
          <MetaField label="Request ID" value={entry.requestId} />
        ) : null}
      </section>

      {content?.jobType ? (
        <Field label="Type">{content.jobType}</Field>
      ) : null}

      {content?.arguments && content.arguments.length > 0 ? (
        <section className={styles.section} data-testid="job-arguments">
          <div className={styles.sectionLabel}>Arguments</div>
          <table className={styles.table}>
            <thead>
              <tr>
                <th className={styles.th}>Name</th>
                <th className={styles.th}>Value</th>
              </tr>
            </thead>
            <tbody>
              {content.arguments.map((arg, i) => (
                <tr key={i}>
                  <td className={`${styles.td} ${styles.argName}`}>{arg.name}</td>
                  <td className={`${styles.td} ${styles.argValue}`}>{arg.value}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      ) : null}

      {content?.errorType ? (
        <section className={styles.section} data-testid="job-error">
          <div className={styles.sectionLabel}>Error</div>
          <div className={styles.errorType}>{content.errorType}</div>
          <div className={styles.errorMessage}>{content.errorMessage}</div>
        </section>
      ) : null}
    </div>
  );
}

function JobExecutionBody({ entry }: { entry: EntryDto }) {
  const content = entry.content as JobExecutionEntryContent;
  const succeeded = content?.state === 'Succeeded';

  return (
    <div className={styles.root} data-testid="job-execution-detail">
      <a className={styles.back} href="#/">
        ← Back to requests
      </a>
      <header className={styles.header}>
        <span className={`${styles.typeBadge} ${styles.typeBadgeExec}`}>EXEC</span>
        <span className={styles.title} data-testid="job-title">
          {shortTypeName(content?.jobType)}.{content?.methodName ?? '—'}
        </span>
        <span
          className={`${styles.stateBadge} ${succeeded ? styles.stateSucceeded : styles.stateFailed}`}
          data-testid="job-state-badge"
        >
          {content?.state ?? '—'}
        </span>
      </header>

      <section className={styles.meta} data-testid="job-meta">
        <MetaField label="Time" value={formatTimestamp(entry.timestamp)} />
        <MetaField label="Duration" value={formatDuration(entry.durationMs)} />
        <MetaField label="Job ID" value={content?.jobId ?? '—'} />
        {content?.enqueueRequestId ? (
          <div>
            <div className={styles.metaLabel}>Enqueue request</div>
            <a
              href={`#/requests/${encodeURIComponent(content.enqueueRequestId)}`}
              className={styles.metaLink}
              data-testid="enqueue-request-link"
            >
              {content.enqueueRequestId}
            </a>
          </div>
        ) : null}
      </section>

      {content?.jobType ? (
        <Field label="Type">{content.jobType}</Field>
      ) : null}

      {content?.errorType ? (
        <section className={styles.section} data-testid="job-error">
          <div className={styles.sectionLabel}>Error</div>
          <div className={styles.errorType}>{content.errorType}</div>
          <div className={styles.errorMessage}>{content.errorMessage}</div>
        </section>
      ) : null}
    </div>
  );
}

// ─── Sub-components ───────────────────────────────────────────────────────────

function MetaField({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div className={styles.metaLabel}>{label}</div>
      <div className={styles.metaValue}>{value}</div>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <section className={styles.section}>
      <div className={styles.sectionLabel}>{label}</div>
      <div className={styles.fieldValue}>{children}</div>
    </section>
  );
}

function shortTypeName(fullName: string | null | undefined): string {
  if (!fullName) return '—';
  const dot = fullName.lastIndexOf('.');
  return dot >= 0 ? fullName.slice(dot + 1) : fullName;
}
