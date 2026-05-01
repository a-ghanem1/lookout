import { Eye } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { listEntries } from '../../api/client';
import type { EntryDto } from '../../api/types';
import { EntryListShell } from '../../components/EntryList/EntryListShell';
import { EntryRow } from '../../components/EntryList/EntryRow';
import { ActiveTagsBar } from '../../components/Tags/TagChip';
import { useTagFilter } from '../../hooks/useTagFilter';
import { formatDuration, formatRelative } from '../../format';
import styles from './JobsPage.module.css';

type KindFilter = 'executions' | 'enqueues' | 'all';
type StatusFilter = 'all' | 'processing' | 'succeeded' | 'failed';

const KIND_LABELS: Record<KindFilter, string> = {
  executions: 'Executions',
  enqueues: 'Enqueues',
  all: 'All',
};

const STATUS_LABELS: Record<StatusFilter, string> = {
  all: 'All',
  processing: 'Processing',
  succeeded: 'Succeeded',
  failed: 'Failed',
};

function shortTypeName(fullName: string | null | undefined): string {
  if (!fullName) return '—';
  const dot = fullName.lastIndexOf('.');
  return dot >= 0 ? fullName.slice(dot + 1) : fullName;
}

function statusChipClass(state: string): string {
  switch (state.toLowerCase()) {
    case 'succeeded': return styles.stateSucceeded;
    case 'failed': return styles.stateFailed;
    case 'processing': return styles.stateProcessing;
    default: return styles.stateNeutral;
  }
}

export function JobsPage() {
  const { activeTags, removeTag, clear: clearTags } = useTagFilter();
  const [kindFilter, setKindFilter] = useState<KindFilter>('executions');
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all');
  const [queueFilter, setQueueFilter] = useState<string>('all');
  const [jobTypeSearch, setJobTypeSearch] = useState('');
  const [debouncedJobTypeSearch, setDebouncedJobTypeSearch] = useState('');
  const [entries, setEntries] = useState<EntryDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | undefined>();
  const [retryCount, setRetryCount] = useState(0);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedJobTypeSearch(jobTypeSearch), 200);
    return () => clearTimeout(timer);
  }, [jobTypeSearch]);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(undefined);

    const tags = activeTags.length > 0 ? activeTags : undefined;
    Promise.all([
      listEntries({ type: 'job-enqueue', tags, limit: 200 }, controller.signal),
      listEntries({ type: 'job-execution', tags, limit: 200 }, controller.signal),
    ])
      .then(([enqResp, execResp]) => {
        const merged = [...enqResp.entries, ...execResp.entries]
          .sort((a, b) => b.timestamp - a.timestamp);
        setEntries(merged);
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

  function handleKindChange(k: KindFilter) {
    setKindFilter(k);
    if (k === 'enqueues') setStatusFilter('all');
  }

  const availableQueues = useMemo(() => {
    const queues = new Set<string>();
    for (const e of entries) {
      const q = e.tags['job.queue'];
      if (q) queues.add(q);
    }
    return Array.from(queues).sort();
  }, [entries]);

  const filtered = useMemo(() => {
    const lowerSearch = debouncedJobTypeSearch.toLowerCase();
    return entries.filter((e) => {
      if (kindFilter === 'executions' && e.type !== 'job-execution') return false;
      if (kindFilter === 'enqueues' && e.type !== 'job-enqueue') return false;

      if (statusFilter !== 'all') {
        const state = (e.tags['job.state'] ?? '').toLowerCase();
        if (state !== statusFilter) return false;
      }

      if (queueFilter !== 'all') {
        const q = e.tags['job.queue'] ?? '';
        if (q !== queueFilter) return false;
      }

      if (lowerSearch) {
        const jobType = (e.tags['job.type'] ?? '').toLowerCase();
        const method = (e.tags['job.method'] ?? '').toLowerCase();
        if (!jobType.includes(lowerSearch) && !method.includes(lowerSearch)) return false;
      }

      return true;
    });
  }, [entries, kindFilter, statusFilter, queueFilter, debouncedJobTypeSearch]);

  const filterSlot = (
    <div className={styles.filterBar}>
      <div className={styles.filterGroup} role="group" aria-label="Kind filter">
        {(['executions', 'enqueues', 'all'] as const).map((k) => (
          <button
            key={k}
            className={`${styles.chip} ${kindFilter === k ? styles.chipActive : ''}`}
            onClick={() => handleKindChange(k)}
            aria-pressed={kindFilter === k}
          >
            {KIND_LABELS[k]}
          </button>
        ))}
      </div>
      {kindFilter !== 'enqueues' && (
        <div className={styles.filterGroup} role="group" aria-label="Status filter">
          {(['all', 'processing', 'succeeded', 'failed'] as const).map((s) => (
            <button
              key={s}
              className={`${styles.chip} ${statusFilter === s ? styles.chipActive : ''}`}
              onClick={() => setStatusFilter(s)}
              aria-pressed={statusFilter === s}
            >
              {STATUS_LABELS[s]}
            </button>
          ))}
        </div>
      )}
      {availableQueues.length > 0 && (
        <select
          className={styles.queueSelect}
          value={queueFilter}
          onChange={(e) => setQueueFilter(e.target.value)}
          aria-label="Queue filter"
        >
          <option value="all">All queues</option>
          {availableQueues.map((q) => (
            <option key={q} value={q}>{q}</option>
          ))}
        </select>
      )}
      <input
        type="search"
        placeholder="Search job type…"
        className={styles.searchInput}
        value={jobTypeSearch}
        onChange={(e) => setJobTypeSearch(e.target.value)}
        aria-label="Search job type"
      />
      <ActiveTagsBar tags={activeTags} onRemove={removeTag} onClear={clearTags} />
    </div>
  );

  return (
    <EntryListShell
      title="Jobs"
      total={entries.length}
      loading={loading}
      error={error}
      items={filtered}
      estimatedRowHeight={40}
      emptyMessage="No Hangfire jobs captured. Enqueue a job to start capturing."
      filterSlot={filterSlot}
      onRetry={() => setRetryCount((c) => c + 1)}
      renderRow={(entry) => {
        const isExecution = entry.type === 'job-execution';
        const state = entry.tags['job.state'] ?? '';
        const jobType = entry.tags['job.type'];
        const method = entry.tags['job.method'] ?? '—';
        const queue = entry.tags['job.queue'];
        const jobLabel = `${shortTypeName(jobType)}.${method}`;

        const handleClick = isExecution
          ? () => { window.location.hash = `/jobs/${entry.id}`; }
          : entry.requestId
          ? () => { window.location.hash = `/requests/${encodeURIComponent(entry.requestId!)}?focus=jobs&entry=${entry.id}`; }
          : undefined;

        return (
          <EntryRow
            timestamp={formatRelative(entry.timestamp)}
            badge={
              <span className={`${styles.kindBadge} ${isExecution ? styles.kindExec : styles.kindEnq}`}>
                {isExecution ? 'EXEC' : 'ENQ'}
              </span>
            }
            summary={
              <span className={styles.summaryRow}>
                <span className={styles.jobLabel}>{jobLabel}</span>
                {isExecution && state && (
                  <span className={`${styles.stateBadge} ${statusChipClass(state)}`}>{state}</span>
                )}
                {queue && <span className={styles.queueLabel}>{queue}</span>}
              </span>
            }
            duration={<span className={styles.durationText}>{formatDuration(entry.durationMs)}</span>}
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
            onClick={handleClick}
          />
        );
      }}
    />
  );
}
