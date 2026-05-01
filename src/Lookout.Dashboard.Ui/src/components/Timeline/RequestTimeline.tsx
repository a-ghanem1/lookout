import { useState } from 'react';
import type { EntryDto } from '../../api/types';
import { formatDuration } from '../../format';
import styles from './RequestTimeline.module.css';

const ROW_TYPES = [
  { key: 'db', label: 'DB', types: ['ef', 'sql'], colorVar: '--color-info-fg' },
  { key: 'http-out', label: 'HTTP', types: ['http-out'], colorVar: '--color-accent-fg' },
  { key: 'cache', label: 'Cache', types: ['cache'], colorVar: '--color-success-fg' },
  { key: 'exception', label: 'Exc', types: ['exception'], colorVar: '--color-error-fg' },
  { key: 'log', label: 'Log', types: ['log'], colorVar: '--color-warn-fg' },
  { key: 'dump', label: 'Dump', types: ['dump'], colorVar: '--color-fg-subtle' },
  { key: 'job', label: 'Jobs', types: ['job-enqueue', 'job-execution'], colorVar: '--color-warn-fg' },
];

const TOOLTIP_W = 252;
const PAD_RATIO = 0.05;

interface BarTooltip {
  clientX: number;
  clientY: number;
  label: string;
  duration: string;
  summary: string;
}

function entryTypeSummary(entry: EntryDto): string {
  const c = entry.content as Record<string, unknown>;
  if (!c) return entry.type;
  if (entry.type === 'ef' || entry.type === 'sql') {
    const text = c.commandText as string;
    return text ? text.slice(0, 80) : 'query';
  }
  if (entry.type === 'http-out') {
    const method = c.method as string;
    const url = c.url as string;
    return `${method ?? ''} ${url ?? ''}`.slice(0, 80);
  }
  if (entry.type === 'cache') {
    const op = c.operation as string;
    const key = c.key as string;
    return `${op ?? ''} ${key ?? ''}`.slice(0, 80);
  }
  if (entry.type === 'exception') {
    return (c.exceptionType as string) ?? 'exception';
  }
  if (entry.type === 'dump') {
    return (c.label as string) ?? (c.valueType as string) ?? 'dump';
  }
  if (entry.type === 'job-enqueue' || entry.type === 'job-execution') {
    return (c.methodName as string) ?? 'job';
  }
  if (entry.type === 'log') {
    return (c.message as string)?.slice(0, 80) ?? 'log';
  }
  return entry.type;
}

interface RequestTimelineProps {
  httpEntry: EntryDto;
  childEntries: EntryDto[];
  onScrollToEntry?: (id: string) => void;
}

export function RequestTimeline({
  httpEntry,
  childEntries,
  onScrollToEntry,
}: RequestTimelineProps) {
  const [tooltip, setTooltip] = useState<BarTooltip | null>(null);

  const requestStart = httpEntry.timestamp;
  const requestDuration = httpEntry.durationMs ?? 0;
  const requestEnd = requestStart + requestDuration;

  const activeRows = ROW_TYPES.filter((row) =>
    childEntries.some((e) => row.types.includes(e.type)),
  );

  if (activeRows.length === 0 || requestDuration <= 0) return null;

  const pad = requestDuration * PAD_RATIO;
  const domainStart = requestStart - pad;
  const domainEnd = requestEnd + pad;
  const domainMs = domainEnd - domainStart;

  function toLeftPct(ts: number) {
    return Math.max(0, Math.min(100, ((ts - domainStart) / domainMs) * 100));
  }
  function toWidthPct(ms: number) {
    return Math.max(0.3, (ms / domainMs) * 100);
  }

  return (
    <div className={styles.root} data-testid="request-timeline">
      <div className={styles.header}>
        <span className={styles.title}>Timeline</span>
      </div>

      <div className={styles.body}>
        {activeRows.map((row) => {
          const rowEntries = childEntries.filter((e) => row.types.includes(e.type));
          return (
            <div key={row.key} className={styles.row}>
              <span className={styles.rowLabel}>{row.label}</span>
              <div className={styles.track}>
                {rowEntries.map((entry) => {
                  const left = toLeftPct(entry.timestamp);
                  const width = Math.min(toWidthPct(entry.durationMs ?? 0), 100 - left);
                  return (
                    <div
                      key={entry.id}
                      className={styles.bar}
                      style={{
                        left: `${left}%`,
                        width: `${width}%`,
                        background: `var(${row.colorVar})`,
                      }}
                      onMouseEnter={(e) => {
                        setTooltip({
                          clientX: e.clientX,
                          clientY: e.clientY,
                          label: row.label,
                          duration: formatDuration(entry.durationMs),
                          summary: entryTypeSummary(entry),
                        });
                      }}
                      onMouseLeave={() => setTooltip(null)}
                      onClick={() => onScrollToEntry?.(entry.id)}
                      data-testid="timeline-bar"
                      data-entry-id={entry.id}
                    />
                  );
                })}
              </div>
            </div>
          );
        })}

        <div className={styles.axisRow}>
          <span className={styles.rowLabel} />
          <div className={styles.axisTrack}>
            <span className={styles.axisLabel}>0ms</span>
            <span className={styles.axisLabel}>{formatDuration(requestDuration)}</span>
          </div>
        </div>
      </div>

      {tooltip && (
        <div
          className={styles.tooltip}
          style={{
            left:
              tooltip.clientX + 12 + TOOLTIP_W > window.innerWidth
                ? tooltip.clientX - TOOLTIP_W - 12
                : tooltip.clientX + 12,
            top: tooltip.clientY - 20,
          }}
        >
          <div className={styles.tooltipType}>{tooltip.label}</div>
          <div className={styles.tooltipDuration}>{tooltip.duration}</div>
          <div className={styles.tooltipSummary}>{tooltip.summary}</div>
        </div>
      )}
    </div>
  );
}
