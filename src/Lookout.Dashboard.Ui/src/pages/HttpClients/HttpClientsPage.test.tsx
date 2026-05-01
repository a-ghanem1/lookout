import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { EntryListResponse } from '../../api/types';
import { HttpClientsPage } from './HttpClientsPage';

function makeHttpOutEntry(
  id: string,
  opts: {
    method?: string;
    host?: string;
    path?: string;
    status?: number | null;
    durationMs?: number;
    requestId?: string;
    errorType?: string;
  } = {},
) {
  const tags: Record<string, string> = {
    'http.method': opts.method ?? 'GET',
    'http.out': 'true',
    'http.url.host': opts.host ?? 'api.example.com',
    'http.url.path': opts.path ?? '/data',
  };
  if (opts.status != null) tags['http.status'] = String(opts.status);
  if (opts.errorType) tags['http.error'] = opts.errorType;

  return {
    id,
    type: 'http-out',
    timestamp: Date.now() - 1000,
    durationMs: opts.durationMs ?? 45,
    requestId: opts.requestId,
    tags,
    content: {
      method: opts.method ?? 'GET',
      url: `https://${opts.host ?? 'api.example.com'}${opts.path ?? '/data'}`,
      statusCode: opts.status ?? null,
      durationMs: opts.durationMs ?? 45,
      requestHeaders: {},
      responseHeaders: {},
    },
  };
}

function makeResponse(entries: unknown[] = []): EntryListResponse {
  return { entries: entries as EntryListResponse['entries'], nextBefore: null };
}

describe('HttpClientsPage', () => {
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
              makeHttpOutEntry('id-1', { method: 'GET', host: 'api.example.com', path: '/users', status: 200, requestId: 'req-x' }),
              makeHttpOutEntry('id-2', { method: 'POST', host: 'payments.io', path: '/charge', status: 422 }),
              makeHttpOutEntry('id-3', { method: 'GET', host: 'cdn.example.com', path: '/img.png', errorType: 'System.Net.Http.HttpRequestException' }),
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

  it('renders entries after fetch', async () => {
    render(<HttpClientsPage />);

    await waitFor(() => {
      expect(screen.getByText('api.example.com')).toBeInTheDocument();
    });
    expect(screen.getByText('payments.io')).toBeInTheDocument();
    expect(screen.getByText('cdn.example.com')).toBeInTheDocument();
  });

  it('renders method badges', async () => {
    render(<HttpClientsPage />);

    await waitFor(() => {
      // "GET" appears in both the method dropdown option and row badges
      expect(screen.getAllByText('GET').length).toBeGreaterThanOrEqual(2);
    });
    // "POST" appears in the dropdown option and the row badge
    expect(screen.getAllByText('POST').length).toBeGreaterThanOrEqual(2);
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

    render(<HttpClientsPage />);

    await waitFor(() => {
      expect(screen.getByTestId('entry-empty-state')).toBeInTheDocument();
    });
  });

  it('sends type=http-out in every fetch', async () => {
    render(<HttpClientsPage />);

    await waitFor(() => expect(capturedUrls.length).toBeGreaterThan(0));

    expect(capturedUrls[0]).toContain('type=http-out');
  });

  it('sends status=200-299 when 2xx filter is clicked', async () => {
    const user = userEvent.setup();
    render(<HttpClientsPage />);

    await waitFor(() => expect(capturedUrls.length).toBeGreaterThan(0));
    capturedUrls.length = 0;

    await user.click(screen.getByRole('button', { name: /^2xx$/i }));

    await waitFor(() => expect(capturedUrls.length).toBeGreaterThan(0));
    expect(capturedUrls[0]).toContain('status=200-299');
  });

  it('sends status=500-599 when 5xx filter is clicked', async () => {
    const user = userEvent.setup();
    render(<HttpClientsPage />);

    await waitFor(() => expect(capturedUrls.length).toBeGreaterThan(0));
    capturedUrls.length = 0;

    await user.click(screen.getByRole('button', { name: /^5xx$/i }));

    await waitFor(() => expect(capturedUrls.length).toBeGreaterThan(0));
    expect(capturedUrls[0]).toContain('status=500-599');
  });

  it('sends errors_only=true when Errors filter is clicked', async () => {
    const user = userEvent.setup();
    render(<HttpClientsPage />);

    await waitFor(() => expect(capturedUrls.length).toBeGreaterThan(0));
    capturedUrls.length = 0;

    await user.click(screen.getByRole('button', { name: /^errors$/i }));

    await waitFor(() => expect(capturedUrls.length).toBeGreaterThan(0));
    expect(capturedUrls[0]).toContain('errors_only=true');
  });

  it('renders ERR pill for network error entries (no status code)', async () => {
    render(<HttpClientsPage />);

    await waitFor(() => {
      expect(screen.getByText('ERR')).toBeInTheDocument();
    });
  });

  it('renders request link for entries with requestId', async () => {
    render(<HttpClientsPage />);

    await waitFor(() => {
      const link = screen.getByRole('link', { name: /view parent request/i });
      expect(link).toHaveAttribute('href', '#/requests/req-x');
    });
  });

  it('renders Background for entries without requestId', async () => {
    render(<HttpClientsPage />);

    await waitFor(() => {
      expect(screen.getAllByText('Background').length).toBeGreaterThan(0);
    });
  });

  it('navigates to #/http-clients/:id when a row is clicked', async () => {
    const user = userEvent.setup();
    render(<HttpClientsPage />);

    await waitFor(() => {
      expect(screen.getByText('api.example.com')).toBeInTheDocument();
    });

    const rows = screen.getAllByRole('button');
    const entryRow = rows.find((r) => r.textContent?.includes('api.example.com') && r.textContent?.includes('/users'));
    if (entryRow) await user.click(entryRow);

    expect(window.location.hash).toBe('#/http-clients/id-1');
  });
});

describe('HttpClientDetail (via HttpClientsPage id prop)', () => {
  const detailEntry = {
    id: 'id-1',
    type: 'http-out',
    timestamp: Date.now() - 5000,
    durationMs: 120,
    requestId: 'req-parent',
    tags: { 'http.status': '200', 'http.method': 'POST', 'http.url.host': 'api.example.com', 'http.url.path': '/orders' },
    content: {
      method: 'POST',
      url: 'https://api.example.com/orders',
      statusCode: 200,
      durationMs: 120,
      requestHeaders: { 'Content-Type': 'application/json', Authorization: 'Bearer token' },
      responseHeaders: { 'Content-Type': 'application/json' },
      requestBody: '{"items":[{"id":1}]}',
      responseBody: '{"orderId":"abc-123","status":"created"}',
    },
  };

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function stubDetail(entry: unknown) {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(JSON.stringify(entry), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
      ) as unknown as typeof fetch,
    );
  }

  it('renders detail view when id prop is passed', async () => {
    stubDetail(detailEntry);
    render(<HttpClientsPage id="id-1" />);

    await waitFor(() => {
      expect(screen.getByText('https://api.example.com/orders')).toBeInTheDocument();
    });
    expect(screen.getByRole('link', { name: /parent request/i })).toBeInTheDocument();
  });

  it('shows request and response headers', async () => {
    stubDetail(detailEntry);
    render(<HttpClientsPage id="id-1" />);

    await waitFor(() => {
      expect(screen.getAllByText('Content-Type').length).toBeGreaterThanOrEqual(2);
    });
    expect(screen.getByText('Authorization')).toBeInTheDocument();
  });

  it('auto-opens body sections when bodies are present', async () => {
    stubDetail(detailEntry);
    render(<HttpClientsPage id="id-1" />);

    await waitFor(() => {
      expect(screen.getAllByText(/request body/i).length).toBeGreaterThan(0);
    });
    expect(screen.getAllByText(/response body/i).length).toBeGreaterThan(0);
  });

  it('shows method and status in header row', async () => {
    stubDetail(detailEntry);
    render(<HttpClientsPage id="id-1" />);

    await waitFor(() => {
      expect(screen.getByText('POST')).toBeInTheDocument();
    });
    expect(screen.getByText('200')).toBeInTheDocument();
  });

  it('back link points to #/http-clients', async () => {
    stubDetail(detailEntry);
    render(<HttpClientsPage id="id-1" />);

    await waitFor(() => {
      const links = screen.getAllByRole('link', { name: /http clients/i });
      expect(links[0]).toHaveAttribute('href', '#/http-clients');
    });
  });

  it('shows loading state while fetch is pending', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})) as unknown as typeof fetch);
    render(<HttpClientsPage id="id-1" />);
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it('shows error state when fetch fails', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response('not found', { status: 404, headers: { 'content-type': 'text/plain' } }),
      ) as unknown as typeof fetch,
    );
    render(<HttpClientsPage id="id-1" />);
    await waitFor(() => {
      expect(screen.getByText(/entry: 404/i)).toBeInTheDocument();
    });
  });

  it('shows error detail block for network errors', async () => {
    stubDetail({
      ...detailEntry,
      requestId: undefined,
      tags: { 'http.error': 'System.Net.Http.HttpRequestException' },
      content: {
        ...detailEntry.content as Record<string, unknown>,
        statusCode: null,
        errorType: 'System.Net.Http.HttpRequestException',
        errorMessage: 'Connection refused',
        requestBody: null,
        responseBody: null,
      },
    });
    render(<HttpClientsPage id="id-1" />);

    await waitFor(() => {
      expect(screen.getByText('System.Net.Http.HttpRequestException')).toBeInTheDocument();
    });
    expect(screen.getByText('Connection refused')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /parent request/i })).not.toBeInTheDocument();
  });
});
