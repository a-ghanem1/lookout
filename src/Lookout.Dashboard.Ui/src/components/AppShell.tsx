import type { ReactNode } from 'react';
import { useTheme } from '../theme/useTheme';
import { LookoutLogo } from './LookoutLogo';
import styles from './AppShell.module.css';

export interface AppShellProps {
  children: ReactNode;
}

export function AppShell({ children }: AppShellProps) {
  const [theme, toggle] = useTheme();
  return (
    <div className={styles.shell}>
      <header className={styles.header}>
        <a className={styles.brand} href="#/" aria-label="Lookout — diagnostics for ASP.NET Core">
          <LookoutLogo className={styles.brandMark} aria-hidden="true" aria-label={undefined} />
          <span className={styles.brandText}>
            <span className={styles.brandWordmark}>lookout</span>
            <span className={styles.brandTagline}>Diagnostics for ASP.NET Core</span>
          </span>
        </a>
        <div className={styles.actions}>
          <button
            type="button"
            className={styles.themeButton}
            onClick={toggle}
            aria-label={`Switch to ${theme === 'dark' ? 'light' : 'dark'} theme`}
          >
            {theme === 'dark' ? 'Light' : 'Dark'}
          </button>
        </div>
      </header>
      <main className={styles.main}>{children}</main>
    </div>
  );
}
