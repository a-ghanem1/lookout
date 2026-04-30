import styles from './LoadingState.module.css';

export function LoadingState() {
  return (
    <div className={styles.root} data-testid="entry-loading-state" aria-label="Loading…">
      <div className={styles.shimmerRow} />
      <div className={styles.shimmerRow} />
      <div className={styles.shimmerRow} />
      <div className={styles.shimmerRow} />
      <div className={styles.shimmerRow} />
    </div>
  );
}
