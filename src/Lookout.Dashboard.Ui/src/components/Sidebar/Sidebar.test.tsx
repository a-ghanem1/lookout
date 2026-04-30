import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { EntryCounts } from '../../api/types';
import { Sidebar } from './Sidebar';

const ZERO_COUNTS: EntryCounts = {
  requests: 0,
  queries: 0,
  exceptions: 0,
  logs: 0,
  cache: 0,
  httpClients: 0,
  jobs: 0,
  dump: 0,
};

const ALL_COUNTS: EntryCounts = {
  requests: 10,
  queries: 5,
  exceptions: 2,
  logs: 20,
  cache: 7,
  httpClients: 3,
  jobs: 4,
  dump: 1,
};

describe('Sidebar', () => {
  it('renders all 8 nav items', () => {
    render(
      <Sidebar
        route={{ name: 'list' }}
        counts={ZERO_COUNTS}
        themeMode="dark"
        onThemeCycle={vi.fn()}
        onSearch={vi.fn()}
      />,
    );

    const nav = screen.getByRole('navigation', { name: /sections/i });
    const links = nav.querySelectorAll('a');
    expect(links).toHaveLength(8);

    const labels = Array.from(links).map((l) => l.textContent?.replace(/\d+/, '').trim());
    expect(labels).toContain('Requests');
    expect(labels).toContain('Queries');
    expect(labels).toContain('Exceptions');
    expect(labels).toContain('Logs');
    expect(labels).toContain('Cache');
    expect(labels).toContain('HTTP clients');
    expect(labels).toContain('Jobs');
    expect(labels).toContain('Dump');
  });

  it('marks the active route with aria-current="page"', () => {
    render(
      <Sidebar
        route={{ name: 'queries' }}
        counts={ZERO_COUNTS}
        themeMode="dark"
        onThemeCycle={vi.fn()}
        onSearch={vi.fn()}
      />,
    );

    const activeLink = screen.getByRole('link', { name: /queries/i, current: 'page' });
    expect(activeLink).toBeInTheDocument();

    const inactiveLink = screen.getByRole('link', { name: /requests/i });
    expect(inactiveLink).not.toHaveAttribute('aria-current');
  });

  it('marks requests as active for the list and detail routes', () => {
    const { rerender } = render(
      <Sidebar
        route={{ name: 'list' }}
        counts={ZERO_COUNTS}
        themeMode="dark"
        onThemeCycle={vi.fn()}
        onSearch={vi.fn()}
      />,
    );
    expect(screen.getByRole('link', { name: /requests/i, current: 'page' })).toBeInTheDocument();

    rerender(
      <Sidebar
        route={{ name: 'detail', id: 'abc' }}
        counts={ZERO_COUNTS}
        themeMode="dark"
        onThemeCycle={vi.fn()}
        onSearch={vi.fn()}
      />,
    );
    expect(screen.getByRole('link', { name: /requests/i, current: 'page' })).toBeInTheDocument();
  });

  it('renders count badges with values from counts prop', () => {
    render(
      <Sidebar
        route={{ name: 'list' }}
        counts={ALL_COUNTS}
        themeMode="dark"
        onThemeCycle={vi.fn()}
        onSearch={vi.fn()}
      />,
    );

    expect(screen.getByTestId('sidebar-count-requests')).toHaveTextContent('10');
    expect(screen.getByTestId('sidebar-count-queries')).toHaveTextContent('5');
    expect(screen.getByTestId('sidebar-count-exceptions')).toHaveTextContent('2');
    expect(screen.getByTestId('sidebar-count-logs')).toHaveTextContent('20');
    expect(screen.getByTestId('sidebar-count-cache')).toHaveTextContent('7');
    expect(screen.getByTestId('sidebar-count-http-clients')).toHaveTextContent('3');
    expect(screen.getByTestId('sidebar-count-jobs')).toHaveTextContent('4');
    expect(screen.getByTestId('sidebar-count-dump')).toHaveTextContent('1');
  });

  it('renders 0 (not —) for zero counts', () => {
    render(
      <Sidebar
        route={{ name: 'list' }}
        counts={ZERO_COUNTS}
        themeMode="dark"
        onThemeCycle={vi.fn()}
        onSearch={vi.fn()}
      />,
    );

    expect(screen.getByTestId('sidebar-count-requests')).toHaveTextContent('0');
    expect(screen.getByTestId('sidebar-count-jobs')).toHaveTextContent('0');
  });

  it('renders the brand link pointing to #/requests', () => {
    render(
      <Sidebar
        route={{ name: 'list' }}
        counts={ZERO_COUNTS}
        themeMode="dark"
        onThemeCycle={vi.fn()}
        onSearch={vi.fn()}
      />,
    );

    const brand = screen.getByRole('link', {
      name: /lookout\s*[—-]\s*diagnostics for asp\.net core/i,
    });
    expect(brand).toHaveAttribute('href', '#/requests');
    const svg = brand.querySelector('svg');
    expect(svg).not.toBeNull();
  });

  it('renders the theme cycle button', () => {
    render(
      <Sidebar
        route={{ name: 'list' }}
        counts={ZERO_COUNTS}
        themeMode="dark"
        onThemeCycle={vi.fn()}
        onSearch={vi.fn()}
      />,
    );

    expect(screen.getByTestId('theme-cycle-button')).toBeInTheDocument();
  });

  it('renders the search button', () => {
    render(
      <Sidebar
        route={{ name: 'list' }}
        counts={ZERO_COUNTS}
        themeMode="system"
        onThemeCycle={vi.fn()}
        onSearch={vi.fn()}
      />,
    );

    expect(screen.getByTestId('sidebar-search-button')).toBeInTheDocument();
  });
});
