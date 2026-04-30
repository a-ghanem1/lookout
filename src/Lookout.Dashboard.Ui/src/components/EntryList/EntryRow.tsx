import type { ReactNode } from 'react';
import styles from './EntryRow.module.css';

interface EntryRowProps {
  timestamp: ReactNode;
  badge?: ReactNode;
  summary: ReactNode;
  duration?: ReactNode;
  requestLink?: ReactNode;
  onClick?: () => void;
  testId?: string;
}

export function EntryRow({
  timestamp,
  badge,
  summary,
  duration,
  requestLink,
  onClick,
  testId,
}: EntryRowProps) {
  const Tag = onClick ? 'button' : 'div';
  return (
    <Tag
      type={onClick ? 'button' : undefined}
      className={`${styles.row} ${onClick ? styles.rowClickable : ''}`}
      onClick={onClick}
      data-testid={testId}
    >
      <span className={styles.timestamp}>{timestamp}</span>
      {badge && <span className={styles.badge}>{badge}</span>}
      <span className={styles.summary}>{summary}</span>
      {duration && <span className={styles.duration}>{duration}</span>}
      {requestLink && <span className={styles.requestLink}>{requestLink}</span>}
    </Tag>
  );
}
