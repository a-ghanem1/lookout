import { render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AppShell } from './AppShell';

describe('AppShell', () => {
  beforeEach(() => {
    vi.stubGlobal('matchMedia', vi.fn(() => ({
      matches: false,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    })));
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(
          JSON.stringify({ requests: 0, queries: 0, exceptions: 0, logs: 0, cache: 0, httpClients: 0, jobs: 0, dump: 0 }),
          { status: 200, headers: { 'content-type': 'application/json' } },
        ),
      ) as unknown as typeof fetch,
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders the brand mark as a labelled link to #/requests', () => {
    render(
      <AppShell route={{ name: 'list' }}>
        <div>content</div>
      </AppShell>,
    );

    const brand = screen.getByRole('link', {
      name: /lookout\s*[—-]\s*diagnostics for asp\.net core/i,
    });
    expect(brand).toHaveAttribute('href', '#/requests');
    const svg = brand.querySelector('svg');
    expect(svg).not.toBeNull();
    expect(svg).toHaveAttribute('aria-hidden', 'true');
  });

  it('renders children inside the main content area', () => {
    render(
      <AppShell route={{ name: 'list' }}>
        <div data-testid="child">hello</div>
      </AppShell>,
    );
    expect(screen.getByTestId('child')).toBeInTheDocument();
  });

  it('renders all 8 sidebar nav items', () => {
    render(
      <AppShell route={{ name: 'list' }}>
        <div>content</div>
      </AppShell>,
    );
    const nav = screen.getByRole('navigation', { name: /sections/i });
    expect(nav.querySelectorAll('a')).toHaveLength(8);
  });
});
