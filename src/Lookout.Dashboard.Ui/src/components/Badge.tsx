import { statusCategory } from '../format';
import styles from './Badge.module.css';

export function StatusBadge({ status }: { status: number }) {
  const cat = statusCategory(status);
  const klass = `status_${cat}` as const;
  return (
    <span className={`${styles.badge} ${styles[klass]}`} data-testid="status-badge">
      {status}
    </span>
  );
}

export function MethodBadge({ method }: { method: string }) {
  return (
    <span className={`${styles.badge} ${styles.method}`} data-testid="method-badge">
      {method}
    </span>
  );
}
