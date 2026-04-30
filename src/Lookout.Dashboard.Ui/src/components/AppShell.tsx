import type { ReactNode } from 'react';
import { useEffect, useState } from 'react';
import { useEntryCounts } from '../hooks/useEntryCounts';
import type { Route } from '../router/hashRouter';
import { useTheme } from '../theme/useTheme';
import { SearchPalette } from './SearchPalette/SearchPalette';
import { Sidebar } from './Sidebar/Sidebar';
import styles from './AppShell.module.css';

export interface AppShellProps {
  route: Route;
  children: ReactNode;
}

export function AppShell({ route, children }: AppShellProps) {
  const [theme, themeMode, cycle] = useTheme();
  const counts = useEntryCounts();
  const [searchOpen, setSearchOpen] = useState(false);

  // Cmd+K / Ctrl+K global shortcut
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key === 'k') {
        e.preventDefault();
        setSearchOpen((v) => !v);
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, []);

  return (
    <div className={styles.shell} data-theme={theme}>
      <Sidebar
        route={route}
        counts={counts}
        themeMode={themeMode}
        onThemeCycle={cycle}
        onSearch={() => setSearchOpen(true)}
      />
      <main className={styles.main}>{children}</main>
      <SearchPalette open={searchOpen} onClose={() => setSearchOpen(false)} />
    </div>
  );
}
