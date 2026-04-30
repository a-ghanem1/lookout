import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { EntryListResponse } from '../../api/types';
import { CachePage } from './CachePage';

function makeCacheEntry(
  id: string,
  opts: {
    operation: string;
    key: string;
    hit?: boolean;
    system?: string;
    requestId?: string;
    durationMs?: number;
    valueType?: string;
  } = { operation: 'Get', key: 'test-key' },
) {
  const tags: Record<string, string> = {
    'cache.system': opts.system ?? 'memory',
    'cache.key': opts.key,
  };
  if (opts.hit !== undefined) tags['cache.hit'] = opts.hit ? 'true' : 'false';
  if (opts.valueType) tags['cache.value-type'] = opts.valueType;

  return {
    id,
    type: 'cache',
    timestamp: Date.now() - 1000,
    durationMs: opts.durationMs ?? 0.5,
    requestId: opts.requestId,
    tags,
    content: {
      operation: opts.operation,
      key: opts.key,
      hit: opts.hit ?? null,
      durationMs: opts.durationMs ?? 0.5,
      valueType: opts.valueType ?? null,
      valueBytes: null,
    },
  };
}

function makeResponse(entries: unknown[] = []): EntryListResponse {
  return { entries: entries as EntryListResponse['entries'], nextBefore: null };
}

const defaultSummary = { hits: 8, misses: 2, sets: 5, removes: 1, hitRatio: 0.8 };

describe('CachePage', () => {
  const capturedUrls: string[] = [];

  beforeEach(() => {
    capturedUrls.length = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        capturedUrls.push(url);

        if (url.includes('cache/summary')) {
          return new Response(JSON.stringify(defaultSummary), {
            status: 200,
            headers: { 'content-type': 'application/json' },
          });
        }

        return new Response(
          JSON.stringify(
            makeResponse([
              makeCacheEntry('id-1', {
                operation: 'Get',
                key: 'user:42',
                hit: true,
                system: 'memory',
                requestId: 'req-abc',
              }),
              makeCacheEntry('id-2', {
                operation: 'Get',
                key: 'session:xyz',
                hit: false,
                system: 'distributed',
              }),
              makeCacheEntry('id-3', { operation: 'Set', key: 'product:7', system: 'memory' }),
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

  it('renders cache entry keys after fetch', async () => {
    render(<CachePage />);

    await waitFor(() => {
      expect(screen.getByText('user:42')).toBeInTheDocument();
    });
    expect(screen.getByText('session:xyz')).toBeInTheDocument();
    expect(screen.getByText('product:7')).toBeInTheDocument();
  });

  it('renders MEM badge for memory cache entries', async () => {
    render(<CachePage />);

    await waitFor(() => {
      expect(screen.getAllByText('MEM').length).toBeGreaterThanOrEqual(1);
    });
  });

  it('renders DIST badge for distributed cache entries', async () => {
    render(<CachePage />);

    await waitFor(() => {
      expect(screen.getByText('DIST')).toBeInTheDocument();
    });
  });

  it('renders Hit pill for get-hit entries', async () => {
    render(<CachePage />);

    await waitFor(() => {
      expect(screen.getByText('Hit')).toBeInTheDocument();
    });
  });

  it('renders Miss pill for get-miss entries', async () => {
    render(<CachePage />);

    await waitFor(() => {
      expect(screen.getByText('Miss')).toBeInTheDocument();
    });
  });

  it('shows hit-ratio header from summary endpoint', async () => {
    render(<CachePage />);

    await waitFor(() => {
      expect(screen.getByTestId('cache-hit-ratio')).toBeInTheDocument();
    });
    expect(screen.getByText(/Hits 8/)).toBeInTheDocument();
    expect(screen.getByText(/Misses 2/)).toBeInTheDocument();
  });

  it('shows empty state when no entries returned', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        if (url.includes('cache/summary')) {
          return new Response(JSON.stringify({ hits: 0, misses: 0, sets: 0, removes: 0, hitRatio: 0 }), {
            status: 200,
            headers: { 'content-type': 'application/json' },
          });
        }
        return new Response(JSON.stringify(makeResponse([])), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        });
      }) as unknown as typeof fetch,
    );

    render(<CachePage />);

    await waitFor(() => {
      expect(screen.getByTestId('entry-empty-state')).toBeInTheDocument();
    });
  });

  it('sends type=cache in fetch', async () => {
    render(<CachePage />);

    await waitFor(() => expect(capturedUrls.some((u) => u.includes('entries?') || u.includes('entries/cache'))).toBe(true));

    const entriesUrl = capturedUrls.find((u) => u.includes('type=cache'));
    expect(entriesUrl).toBeDefined();
  });

  it('filters to MEM entries when MEM chip is clicked', async () => {
    const user = userEvent.setup();
    render(<CachePage />);

    await waitFor(() => {
      expect(screen.getByText('user:42')).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button', { name: /^MEM$/i }));

    await waitFor(() => {
      expect(screen.getByText('user:42')).toBeInTheDocument();
      expect(screen.queryByText('session:xyz')).not.toBeInTheDocument();
    });
  });

  it('filters to Hit entries when Hit chip is clicked', async () => {
    const user = userEvent.setup();
    render(<CachePage />);

    await waitFor(() => {
      expect(screen.getByText('session:xyz')).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button', { name: /^Hit$/i }));

    await waitFor(() => {
      expect(screen.getByText('user:42')).toBeInTheDocument();
      expect(screen.queryByText('session:xyz')).not.toBeInTheDocument();
      expect(screen.queryByText('product:7')).not.toBeInTheDocument();
    });
  });

  it('expands row to show full key when clicked', async () => {
    const user = userEvent.setup();
    render(<CachePage />);

    await waitFor(() => {
      expect(screen.getByText('user:42')).toBeInTheDocument();
    });

    const rows = screen.getAllByRole('button');
    const entryRow = rows.find((r) => r.textContent?.includes('user:42'));
    if (entryRow) await user.click(entryRow);

    await waitFor(() => {
      expect(screen.getByTestId('expand-id-1')).toBeInTheDocument();
    });
  });

  it('renders request link for entries with requestId', async () => {
    render(<CachePage />);

    await waitFor(() => {
      const link = screen.getByRole('link', { name: /view parent request/i });
      expect(link).toHaveAttribute('href', '#/requests/req-abc');
    });
  });

  it('renders Background for entries without requestId', async () => {
    render(<CachePage />);

    await waitFor(() => {
      expect(screen.getAllByText('Background').length).toBeGreaterThan(0);
    });
  });
});
