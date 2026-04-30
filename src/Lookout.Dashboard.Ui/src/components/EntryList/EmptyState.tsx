import styles from './EmptyState.module.css';

interface EmptyStateProps {
  message?: string;
}

export function EmptyState({ message = 'No entries captured yet.' }: EmptyStateProps) {
  return (
    <div className={styles.root} data-testid="entry-empty-state">
      <svg
        className={styles.icon}
        xmlns="http://www.w3.org/2000/svg"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.5"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true"
      >
        <circle cx="12" cy="12" r="9" />
        <line x1="12" y1="8" x2="12" y2="12" />
        <line x1="12" y1="16" x2="12.01" y2="16" />
      </svg>
      <p className={styles.message}>{message}</p>
    </div>
  );
}
