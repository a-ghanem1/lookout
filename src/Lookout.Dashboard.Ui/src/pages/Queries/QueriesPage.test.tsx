import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { EntryListResponse } from '../../api/types';
import { QueriesPage } from './QueriesPage';

function makeEfEntry(id: string, sql: string, durationMs: number) {
  return {
    id,
    type: 'ef',
    timestamp: Date.now() - 1000,
    durationMs,
    requestId: 'req-abc',
    tags: { 'db.system': 'ef' },
    content: { commandText: sql, durationMs, parameters: [], stack: [], commandType: 'Reader' },
  };
}

function makeSqlEntry(id: string, sql: string, durationMs: number) {
  return {
    id,
    type: 'sql',
    timestamp: Date.now() - 2000,
    durationMs,
    requestId: undefined,
    tags: { 'db.system': 'sql' },
    content: { commandText: sql, durationMs, parameters: [], stack: [] },
  };
}

function makeResponse(entries: unknown[] = []): EntryListResponse {
  return { entries: entries as EntryListResponse['entries'], nextBefore: null };
}

describe('QueriesPage', () => {
  const capturedUrls: string[] = [];

  beforeEach(() => {
    capturedUrls.length = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        capturedUrls.push(typeof input === 'string' ? input : input.toString());
        return new Response(
          JSON.stringify(
            makeResponse([
              makeEfEntry('id-ef1', 'SELECT * FROM orders WHERE id = @p0', 750),
              makeSqlEntry('id-sql1', 'SELECT count(*) FROM products', 12),
            ]),
          ),
          { status: 200, headers: { 'content-type': 'application/json' } },
        );
      }) as unknown as typeof fetch,
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows loading state initially then renders entries', async () => {
    render(<QueriesPage />);

    await waitFor(() => {
      expect(screen.getByText(/SELECT \* FROM orders/i)).toBeInTheDocument();
    });

    expect(screen.getByText(/SELECT count\(\*\) FROM products/i)).toBeInTheDocument();
  });

  it('renders EF badge for ef entries and SQL badge for sql entries', async () => {
    render(<QueriesPage />);

    await waitFor(() => {
      // "EF" and "SQL" appear in both the filter chip buttons and the row badges
      expect(screen.getAllByText('EF').length).toBeGreaterThanOrEqual(2);
    });
    expect(screen.getAllByText('SQL').length).toBeGreaterThanOrEqual(2);
  });

  it('shows empty state when no entries returned', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(JSON.stringify(makeResponse([])), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
      ) as unknown as typeof fetch,
    );

    render(<QueriesPage />);

    await waitFor(() => {
      expect(screen.getByTestId('entry-empty-state')).toBeInTheDocument();
    });
  });

  it('sends type=ef,sql by default (All source)', async () => {
    render(<QueriesPage />);

    await waitFor(() => expect(capturedUrls.length).toBeGreaterThan(0));

    expect(capturedUrls[0]).toContain('type=ef%2Csql');
  });

  it('sends type=ef when EF source filter is clicked', async () => {
    const user = userEvent.setup();
    render(<QueriesPage />);

    await waitFor(() => expect(capturedUrls.length).toBeGreaterThan(0));
    capturedUrls.length = 0;

    await user.click(screen.getByRole('button', { name: /^ef$/i }));

    await waitFor(() => expect(capturedUrls.length).toBeGreaterThan(0));
    expect(capturedUrls[0]).toContain('type=ef');
    expect(capturedUrls[0]).not.toContain('ef%2Csql');
  });

  it('sends type=sql when SQL source filter is clicked', async () => {
    const user = userEvent.setup();
    render(<QueriesPage />);

    await waitFor(() => expect(capturedUrls.length).toBeGreaterThan(0));
    capturedUrls.length = 0;

    await user.click(screen.getByRole('button', { name: /^sql$/i }));

    await waitFor(() => expect(capturedUrls.length).toBeGreaterThan(0));
    expect(capturedUrls[0]).toContain('type=sql');
  });

  it('sends min_duration_ms=1000 when >1s duration filter is clicked', async () => {
    const user = userEvent.setup();
    render(<QueriesPage />);

    await waitFor(() => expect(capturedUrls.length).toBeGreaterThan(0));
    capturedUrls.length = 0;

    await user.click(screen.getByRole('button', { name: />1s/i }));

    await waitFor(() => expect(capturedUrls.length).toBeGreaterThan(0));
    expect(capturedUrls[0]).toContain('min_duration_ms=1000');
  });

  it('always sends sort=duration', async () => {
    render(<QueriesPage />);

    await waitFor(() => expect(capturedUrls.length).toBeGreaterThan(0));

    expect(capturedUrls[0]).toContain('sort=duration');
  });

  it('shows "Background" for entries without requestId', async () => {
    render(<QueriesPage />);

    await waitFor(() => {
      expect(screen.getAllByText('Background').length).toBeGreaterThan(0);
    });
  });

  it('shows request link for entries with requestId', async () => {
    render(<QueriesPage />);

    await waitFor(() => {
      const links = screen.getAllByRole('link', { name: /view parent request/i });
      expect(links.length).toBeGreaterThan(0);
      expect(links[0]).toHaveAttribute('href', '#/requests/req-abc');
    });
  });

  it('navigates to #/queries/:id when a row is clicked', async () => {
    const user = userEvent.setup();
    render(<QueriesPage />);

    await waitFor(() => {
      expect(screen.getByText(/SELECT \* FROM orders/i)).toBeInTheDocument();
    });

    const rows = screen.getAllByRole('button');
    const entryRow = rows.find((r) => r.textContent?.includes('SELECT * FROM orders'));
    if (entryRow) await user.click(entryRow);

    expect(window.location.hash).toBe('#/queries/id-ef1');
  });

  it('shows footer with showing count', async () => {
    render(<QueriesPage />);

    await waitFor(() => {
      expect(screen.getByTestId('entry-list-footer')).toBeInTheDocument();
    });
  });
});
