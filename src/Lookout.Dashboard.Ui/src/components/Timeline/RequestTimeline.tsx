import { useRef, useState } from 'react';
import type { EntryDto } from '../../api/types';
import { formatDuration } from '../../format';
import styles from './RequestTimeline.module.css';

const ROW_TYPES = [
  { key: 'db', label: 'DB', types: ['ef', 'sql'], colorVar: '--color-info-fg' },
  { key: 'http-out', label: 'HTTP', types: ['http-out'], colorVar: '--color-accent-fg' },
  { key: 'cache', label: 'Cache', types: ['cache'], colorVar: '--color-success-fg' },
  { key: 'exception', label: 'Exc', types: ['exception'], colorVar: '--color-error-fg' },
  { key: 'dump', label: 'Dump', types: ['dump'], colorVar: '--color-fg-subtle' },
  { key: 'job', label: 'Jobs', types: ['job-enqueue', 'job-execution'], colorVar: '--color-warn-fg' },
];

const ROW_HEIGHT = 20;
const LABEL_WIDTH = 48;
const PAD_RATIO = 0.05;

interface BarTooltip {
  x: number;
  y: number;
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
  showLogs?: boolean;
  onToggleLogs?: () => void;
  onScrollToEntry?: (id: string) => void;
}

export function RequestTimeline({
  httpEntry,
  childEntries,
  showLogs = false,
  onToggleLogs,
  onScrollToEntry,
}: RequestTimelineProps) {
  const svgRef = useRef<SVGSVGElement>(null);
  const [tooltip, setTooltip] = useState<BarTooltip | null>(null);

  const requestStart = httpEntry.timestamp;
  const requestDuration = httpEntry.durationMs ?? 0;
  const requestEnd = requestStart + requestDuration;

  const allEntries = showLogs
    ? childEntries
    : childEntries.filter((e) => e.type !== 'log');

  const activeRows = ROW_TYPES.filter((row) =>
    allEntries.some((e) => row.types.includes(e.type)),
  );

  if (activeRows.length === 0 || requestDuration <= 0) return null;

  const svgHeight = activeRows.length * ROW_HEIGHT + 24; // 24 for axis
  const pad = requestDuration * PAD_RATIO;
  const domainStart = requestStart - pad;
  const domainEnd = requestEnd + pad;
  const domainMs = domainEnd - domainStart;

  function toPercent(ts: number) {
    return ((ts - domainStart) / domainMs) * 100;
  }
  function durationPercent(ms: number) {
    return (ms / domainMs) * 100;
  }

  return (
    <div className={styles.root} data-testid="request-timeline">
      <div className={styles.header}>
        <span className={styles.title}>Timeline</span>
        {onToggleLogs && (
          <button
            type="button"
            className={styles.logsToggle}
            onClick={onToggleLogs}
            data-testid="timeline-show-logs"
          >
            {showLogs ? 'Hide logs' : 'Show logs'}
          </button>
        )}
      </div>

      <div className={styles.svgWrap}>
        <svg
          ref={svgRef}
          className={styles.svg}
          height={svgHeight}
          aria-label="Request timeline"
          role="img"
        >
          {/* Row labels */}
          {activeRows.map((row, ri) => (
            <text
              key={row.key}
              x={LABEL_WIDTH - 4}
              y={ri * ROW_HEIGHT + ROW_HEIGHT / 2 + 4}
              className={styles.rowLabel}
              textAnchor="end"
            >
              {row.label}
            </text>
          ))}

          {/* Bars */}
          {activeRows.map((row, ri) => {
            const rowEntries = allEntries.filter((e) => row.types.includes(e.type));
            return (
              <g key={row.key} transform={`translate(${LABEL_WIDTH},0)`}>
                {rowEntries.map((entry) => {
                  const startPct = toPercent(entry.timestamp);
                  const durPct = Math.max(durationPercent(entry.durationMs ?? 0), 0.3);
                  const y = ri * ROW_HEIGHT + 4;
                  return (
                    <rect
                      key={entry.id}
                      x={`${startPct}%`}
                      y={y}
                      width={`${durPct}%`}
                      height={ROW_HEIGHT - 8}
                      rx={2}
                      style={{ fill: `var(${row.colorVar})`, opacity: 0.85 }}
                      className={styles.bar}
                      onMouseEnter={(e) => {
                        const rect = svgRef.current?.getBoundingClientRect();
                        if (!rect) return;
                        setTooltip({
                          x: e.clientX - rect.left,
                          y: ri * ROW_HEIGHT,
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
              </g>
            );
          })}

          {/* Axis line */}
          <line
            x1={LABEL_WIDTH}
            y1={activeRows.length * ROW_HEIGHT}
            x2="100%"
            y2={activeRows.length * ROW_HEIGHT}
            className={styles.axisLine}
          />

          {/* Axis labels */}
          <text x={LABEL_WIDTH} y={svgHeight - 4} className={styles.axisLabel}>0ms</text>
          <text x="100%" y={svgHeight - 4} className={styles.axisLabel} textAnchor="end">
            {formatDuration(requestDuration)}
          </text>
        </svg>

        {tooltip && (
          <div
            className={styles.tooltip}
            style={{ left: tooltip.x + 12, top: tooltip.y }}
          >
            <div className={styles.tooltipType}>{tooltip.label}</div>
            <div className={styles.tooltipDuration}>{tooltip.duration}</div>
            <div className={styles.tooltipSummary}>{tooltip.summary}</div>
          </div>
        )}
      </div>
    </div>
  );
}
