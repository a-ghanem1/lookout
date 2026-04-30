import type { ReactNode } from 'react';
import { useEntryCounts } from '../hooks/useEntryCounts';
import type { Route } from '../router/hashRouter';
import { useTheme } from '../theme/useTheme';
import { Sidebar } from './Sidebar/Sidebar';
import styles from './AppShell.module.css';

export interface AppShellProps {
  route: Route;
  children: ReactNode;
}

export function AppShell({ route, children }: AppShellProps) {
  const [theme, toggle] = useTheme();
  const counts = useEntryCounts();

  return (
    <div className={styles.shell}>
      <Sidebar route={route} counts={counts} theme={theme} onThemeToggle={toggle} />
      <main className={styles.main}>{children}</main>
    </div>
  );
}
