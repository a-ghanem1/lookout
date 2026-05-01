import { useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { IDE_CHANGED_EVENT, IDE_STORAGE_KEY, readIde } from '../../hooks/useIde';
import type { IdePreference } from '../../hooks/useIde';
import { Search } from 'lucide-react';
import { CacheIcon } from '../Icons/section/CacheIcon';
import { DumpIcon } from '../Icons/section/DumpIcon';
import { ExceptionsIcon } from '../Icons/section/ExceptionsIcon';
import { HttpClientsIcon } from '../Icons/section/HttpClientsIcon';
import { JobsIcon } from '../Icons/section/JobsIcon';
import { LogsIcon } from '../Icons/section/LogsIcon';
import { QueriesIcon } from '../Icons/section/QueriesIcon';
import { RequestsIcon } from '../Icons/section/RequestsIcon';
import { LookoutLogo } from '../LookoutLogo';
import type { EntryCounts } from '../../api/types';
import { deleteAllEntries } from '../../api/client';
import type { Route } from '../../router/hashRouter';
import { href } from '../../router/hashRouter';
import type { ThemeMode } from '../../theme/useTheme';
import styles from './Sidebar.module.css';


interface SidebarProps {
  route: Route;
  counts: EntryCounts;
  themeMode: ThemeMode;
  onThemeCycle: () => void;
  onSearch: () => void;
}

interface NavItem {
  label: string;
  icon: ReactNode;
  route: Route;
  count: number;
  activeFor: Route['name'][];
  alert?: boolean;
}

const THEME_LABELS: Record<ThemeMode, string> = {
  system: 'System',
  light: 'Light',
  dark: 'Dark',
};

const THEME_NEXT: Record<ThemeMode, string> = {
  system: 'light',
  light: 'dark',
  dark: 'system',
};

export function Sidebar({ route, counts, themeMode, onThemeCycle, onSearch }: SidebarProps) {
  const [ide, setIde] = useState<IdePreference>(readIde);
  const [showClearModal, setShowClearModal] = useState(false);
  const [clearing, setClearing] = useState(false);
  const [toast, setToast] = useState<string | null>(null);
  const toastTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const showToast = (msg: string) => {
    setToast(msg);
    if (toastTimerRef.current) clearTimeout(toastTimerRef.current);
    toastTimerRef.current = setTimeout(() => setToast(null), 3000);
  };

  const handleClear = async () => {
    setClearing(true);
    try {
      const result = await deleteAllEntries();
      setShowClearModal(false);
      showToast(`Cleared ${result.deleted} entries.`);
    } catch {
      showToast('Clear failed. Try again.');
    } finally {
      setClearing(false);
    }
  };

  const handleIdeChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const v = e.target.value as IdePreference;
    setIde(v);
    try { localStorage.setItem(IDE_STORAGE_KEY, v); } catch { /* ignore */ }
    window.dispatchEvent(new CustomEvent(IDE_CHANGED_EVENT, { detail: v }));
  };

  const items: NavItem[] = [
    {
      label: 'Requests',
      icon: <RequestsIcon />,
      route: { name: 'list' },
      count: counts.requests,
      activeFor: ['list', 'detail'],
    },
    {
      label: 'Queries',
      icon: <QueriesIcon />,
      route: { name: 'queries' },
      count: counts.queries,
      activeFor: ['queries', 'query-detail'],
    },
    {
      label: 'Exceptions',
      icon: <ExceptionsIcon />,
      route: { name: 'exceptions' },
      count: counts.exceptions,
      activeFor: ['exceptions', 'exception-detail'],
      alert: counts.exceptions > 0,
    },
    {
      label: 'Logs',
      icon: <LogsIcon />,
      route: { name: 'logs' },
      count: counts.logs,
      activeFor: ['logs'],
    },
    {
      label: 'Cache',
      icon: <CacheIcon />,
      route: { name: 'cache' },
      count: counts.cache,
      activeFor: ['cache'],
    },
    {
      label: 'HTTP clients',
      icon: <HttpClientsIcon />,
      route: { name: 'http-clients' },
      count: counts.httpClients,
      activeFor: ['http-clients', 'http-client-detail'],
    },
    {
      label: 'Jobs',
      icon: <JobsIcon />,
      route: { name: 'jobs' },
      count: counts.jobs,
      activeFor: ['jobs', 'job'],
    },
    {
      label: 'Dump',
      icon: <DumpIcon />,
      route: { name: 'dump' },
      count: counts.dump,
      activeFor: ['dump'],
    },
  ];

  return (
    <>
      <aside className={styles.sidebar} aria-label="Navigation">
        <a
          className={styles.brand}
          href="#/requests"
          aria-label="Lookout — diagnostics for ASP.NET Core"
        >
          <LookoutLogo className={styles.brandMark} aria-hidden="true" aria-label={undefined} />
          <span className={styles.brandWordmark}>lookout</span>
        </a>

        <button
          type="button"
          className={styles.searchButton}
          onClick={onSearch}
          data-testid="sidebar-search-button"
          aria-label="Search (⌘K)"
        >
          <Search size={14} strokeWidth={1.75} className={styles.searchIcon} aria-hidden />
          <span className={styles.searchLabel}>Search…</span>
          <kbd className={styles.searchKbd}>⌘K</kbd>
        </button>

        <nav className={styles.nav} aria-label="Sections">
          {items.map((item) => (
            <SidebarItem
              key={item.label}
              label={item.label}
              icon={item.icon}
              itemHref={href(item.route)}
              count={item.count}
              active={item.activeFor.includes(route.name)}
              alert={item.alert}
            />
          ))}
        </nav>

        <div className={styles.footer}>
          <div className={styles.footerRow}>
            <button
              type="button"
              className={styles.themeButton}
              onClick={onThemeCycle}
              aria-label={`Theme: ${THEME_LABELS[themeMode]}. Click to switch to ${THEME_NEXT[themeMode]}`}
              data-testid="theme-cycle-button"
            >
              {themeMode === 'system' ? '◐' : themeMode === 'light' ? '☀' : '☽'}
              <span>{THEME_LABELS[themeMode]}</span>
            </button>
            <button
              type="button"
              className={styles.clearButton}
              onClick={() => setShowClearModal(true)}
              aria-label="Clear all captured entries"
              data-testid="clear-all-button"
            >
              Clear
            </button>
          </div>
          <div className={styles.ideRow}>
            <span className={styles.ideLabel}>IDE</span>
            <select
              className={styles.ideSelect}
              value={ide}
              onChange={handleIdeChange}
              aria-label="IDE preference for stack frame links"
              data-testid="ide-select"
            >
              <option value="none">None</option>
              <option value="vscode">VS Code</option>
              <option value="rider">Rider</option>
            </select>
          </div>
        </div>
      </aside>

      {showClearModal && (
        <ClearModal
          onConfirm={handleClear}
          onCancel={() => setShowClearModal(false)}
          clearing={clearing}
        />
      )}

      {toast && (
        <div className={styles.toast} role="status" data-testid="toast">
          {toast}
        </div>
      )}
    </>
  );
}

interface SidebarItemProps {
  label: string;
  icon: ReactNode;
  itemHref: string;
  count: number;
  active: boolean;
  alert?: boolean;
}

function SidebarItem({ label, icon, itemHref, count, active, alert }: SidebarItemProps) {
  return (
    <a
      href={itemHref}
      className={`${styles.item} ${active ? styles.itemActive : ''}`}
      aria-current={active ? 'page' : undefined}
    >
      <span className={styles.itemIcon}>{icon}</span>
      <span className={styles.itemLabel}>{label}</span>
      <span
        className={`${styles.itemCount} ${alert ? styles.itemCountAlert : ''}`}
        data-testid={`sidebar-count-${label.toLowerCase().replace(/\s+/g, '-')}`}
      >
        {count}
      </span>
    </a>
  );
}

function ClearModal({
  onConfirm,
  onCancel,
  clearing,
}: {
  onConfirm: () => void;
  onCancel: () => void;
  clearing: boolean;
}) {
  return (
    <div className={styles.modalBackdrop} onClick={onCancel}>
      <div
        className={styles.modal}
        onClick={(e) => e.stopPropagation()}
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="clear-modal-title"
        data-testid="clear-modal"
      >
        <h2 id="clear-modal-title" className={styles.modalTitle}>Clear all entries?</h2>
        <p className={styles.modalBody}>This cannot be undone.</p>
        <div className={styles.modalActions}>
          <button
            type="button"
            className={styles.modalCancel}
            onClick={onCancel}
            disabled={clearing}
          >
            Cancel
          </button>
          <button
            type="button"
            className={styles.modalConfirm}
            onClick={onConfirm}
            disabled={clearing}
            data-testid="clear-modal-confirm"
          >
            {clearing ? 'Clearing…' : 'Clear all'}
          </button>
        </div>
      </div>
    </div>
  );
}
