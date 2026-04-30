import type { ReactNode } from 'react';
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
import type { Route } from '../../router/hashRouter';
import { href } from '../../router/hashRouter';
import styles from './Sidebar.module.css';

interface SidebarProps {
  route: Route;
  counts: EntryCounts;
  theme: 'light' | 'dark';
  onThemeToggle: () => void;
}

interface NavItem {
  label: string;
  icon: ReactNode;
  route: Route;
  count: number;
  activeFor: Route['name'][];
}

export function Sidebar({ route, counts, theme, onThemeToggle }: SidebarProps) {
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
    <aside className={styles.sidebar} aria-label="Navigation">
      <a
        className={styles.brand}
        href="#/requests"
        aria-label="Lookout — diagnostics for ASP.NET Core"
      >
        <LookoutLogo className={styles.brandMark} aria-hidden="true" aria-label={undefined} />
        <span className={styles.brandWordmark}>lookout</span>
      </a>

      <nav className={styles.nav} aria-label="Sections">
        {items.map((item) => (
          <SidebarItem
            key={item.label}
            label={item.label}
            icon={item.icon}
            itemHref={href(item.route)}
            count={item.count}
            active={item.activeFor.includes(route.name)}
          />
        ))}
      </nav>

      <div className={styles.footer}>
        <button
          type="button"
          className={styles.themeButton}
          onClick={onThemeToggle}
          aria-label={`Switch to ${theme === 'dark' ? 'light' : 'dark'} theme`}
        >
          {theme === 'dark' ? 'Light' : 'Dark'}
        </button>
      </div>
    </aside>
  );
}

interface SidebarItemProps {
  label: string;
  icon: ReactNode;
  itemHref: string;
  count: number;
  active: boolean;
}

function SidebarItem({ label, icon, itemHref, count, active }: SidebarItemProps) {
  return (
    <a
      href={itemHref}
      className={`${styles.item} ${active ? styles.itemActive : ''}`}
      aria-current={active ? 'page' : undefined}
    >
      <span className={styles.itemIcon}>{icon}</span>
      <span className={styles.itemLabel}>{label}</span>
      <span className={styles.itemCount} data-testid={`sidebar-count-${label.toLowerCase().replace(/\s+/g, '-')}`}>
        {count}
      </span>
    </a>
  );
}
