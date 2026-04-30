import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { EntryListResponse } from '../api/types';
import { RequestList } from './RequestList';

function makeResponse(extraTags: Record<string, string> = {}): EntryListResponse {
  return {
    entries: [
      {
        id: '11111111-1111-1111-1111-111111111111',
        type: 'http',
        timestamp: Date.now() - 2000,
        durationMs: 42.3,
        tags: {
          'http.method': 'GET',
          'http.path': '/weatherforecast',
          'http.status': '200',
          ...extraTags,
        },
        content: {
          method: 'GET',
          path: '/weatherforecast',
          queryString: '',
          statusCode: 200,
          durationMs: 42.3,
          requestHeaders: {},
          responseHeaders: {},
        },
      },
      {
        id: '22222222-2222-2222-2222-222222222222',
        type: 'http',
        timestamp: Date.now() - 1000,
        durationMs: 1200,
        tags: {
          'http.method': 'POST',
          'http.path': '/orders',
          'http.status': '500',
        },
        content: {
          method: 'POST',
          path: '/orders',
          queryString: '',
          statusCode: 500,
          durationMs: 1200,
          requestHeaders: {},
          responseHeaders: {},
        },
      },
    ],
    nextBefore: null,
  };
}

describe('RequestList', () => {
  const urls: string[] = [];

  beforeEach(() => {
    urls.length = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        urls.push(url);
        return new Response(JSON.stringify(makeResponse()), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        });
      }) as unknown as typeof fetch,
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders captured requests', async () => {
    render(<RequestList />);
    await waitFor(() => {
      expect(screen.getAllByTestId('request-row')).toHaveLength(2);
    });
    expect(screen.getByText('/weatherforecast')).toBeInTheDocument();
    expect(screen.getByText('/orders')).toBeInTheDocument();
  });

  it('sends filter values to the API', async () => {
    const user = userEvent.setup();
    render(<RequestList />);
    await waitFor(() => expect(screen.getAllByTestId('request-row')).toHaveLength(2));

    await user.selectOptions(screen.getByLabelText(/method/i), 'POST');
    await user.type(screen.getByLabelText(/path/i), 'orders');

    await waitFor(() => {
      const last = urls[urls.length - 1]!;
      expect(last).toContain('method=POST');
      expect(last).toContain('path=orders');
    });
  });

  it('shows db.count badge when tag is present', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(JSON.stringify(makeResponse({ 'db.count': '7' })), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
      ) as unknown as typeof fetch,
    );
    render(<RequestList />);
    await waitFor(() => {
      expect(screen.getByTestId('db-count-badge')).toBeInTheDocument();
    });
    expect(screen.getByTestId('db-count-badge')).toHaveTextContent('db: 7');
  });

  it('shows N+1 badge when n1.detected=true', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(
          JSON.stringify(makeResponse({ 'db.count': '5', 'n1.detected': 'true' })),
          { status: 200, headers: { 'content-type': 'application/json' } },
        ),
      ) as unknown as typeof fetch,
    );
    render(<RequestList />);
    await waitFor(() => {
      expect(screen.getByTestId('n1-badge')).toBeInTheDocument();
    });
    expect(screen.getByTestId('n1-badge')).toHaveTextContent('N+1');
  });

  it('does not show N+1 badge when n1.detected is absent', async () => {
    render(<RequestList />);
    await waitFor(() => expect(screen.getAllByTestId('request-row')).toHaveLength(2));
    expect(screen.queryByTestId('n1-badge')).not.toBeInTheDocument();
  });

  it('shows an empty state when no entries come back', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        async () =>
          new Response(JSON.stringify({ entries: [], nextBefore: null }), {
            status: 200,
            headers: { 'content-type': 'application/json' },
          }),
      ) as unknown as typeof fetch,
    );
    render(<RequestList />);
    await waitFor(() => {
      expect(screen.getByTestId('empty-state')).toBeInTheDocument();
    });
  });

  it('shows http-out count badge when http.out.count tag is present and > 0', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(JSON.stringify(makeResponse({ 'http.out.count': '3' })), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
      ) as unknown as typeof fetch,
    );
    render(<RequestList />);
    await waitFor(() => {
      expect(screen.getByTestId('http-out-count-badge')).toBeInTheDocument();
    });
    expect(screen.getByTestId('http-out-count-badge')).toHaveTextContent('http: 3');
  });

  it('does not show http-out count badge when tag is 0', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(JSON.stringify(makeResponse({ 'http.out.count': '0' })), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
      ) as unknown as typeof fetch,
    );
    render(<RequestList />);
    await waitFor(() => expect(screen.getAllByTestId('request-row')).toHaveLength(2));
    expect(screen.queryByTestId('http-out-count-badge')).not.toBeInTheDocument();
  });

  it('shows cache count badge when cache.count tag is present and > 0', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(JSON.stringify(makeResponse({ 'cache.count': '5' })), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
      ) as unknown as typeof fetch,
    );
    render(<RequestList />);
    await waitFor(() => {
      expect(screen.getByTestId('cache-count-badge')).toBeInTheDocument();
    });
    expect(screen.getByTestId('cache-count-badge')).toHaveTextContent('cache: 5');
  });

  it('does not show cache count badge when tag is absent', async () => {
    render(<RequestList />);
    await waitFor(() => expect(screen.getAllByTestId('request-row')).toHaveLength(2));
    expect(screen.queryByTestId('cache-count-badge')).not.toBeInTheDocument();
  });

  it('renders all badges together when all tags are present', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(
          JSON.stringify(
            makeResponse({ 'db.count': '2', 'n1.detected': 'true', 'http.out.count': '1', 'cache.count': '4' }),
          ),
          { status: 200, headers: { 'content-type': 'application/json' } },
        ),
      ) as unknown as typeof fetch,
    );
    render(<RequestList />);
    await waitFor(() => {
      expect(screen.getByTestId('db-count-badge')).toBeInTheDocument();
    });
    expect(screen.getByTestId('n1-badge')).toBeInTheDocument();
    expect(screen.getByTestId('http-out-count-badge')).toHaveTextContent('http: 1');
    expect(screen.getByTestId('cache-count-badge')).toHaveTextContent('cache: 4');
  });

  it('shows exception badge when exception=true tag present', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(JSON.stringify(makeResponse({ exception: 'true' })), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
      ) as unknown as typeof fetch,
    );
    render(<RequestList />);
    await waitFor(() => {
      expect(screen.getByTestId('exception-badge')).toBeInTheDocument();
    });
    expect(screen.getByTestId('exception-badge')).toHaveTextContent('error');
  });

  it('does not show exception badge when exception tag is absent', async () => {
    render(<RequestList />);
    await waitFor(() => expect(screen.getAllByTestId('request-row')).toHaveLength(2));
    expect(screen.queryByTestId('exception-badge')).not.toBeInTheDocument();
  });

  it('shows warn badge when log.maxLevel=Warning', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(JSON.stringify(makeResponse({ 'log.maxLevel': 'Warning' })), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
      ) as unknown as typeof fetch,
    );
    render(<RequestList />);
    await waitFor(() => {
      expect(screen.getByTestId('log-warn-badge')).toBeInTheDocument();
    });
    expect(screen.getByTestId('log-warn-badge')).toHaveTextContent('log: warn');
  });

  it('shows error badge when log.maxLevel=Error', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(JSON.stringify(makeResponse({ 'log.maxLevel': 'Error' })), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
      ) as unknown as typeof fetch,
    );
    render(<RequestList />);
    await waitFor(() => {
      expect(screen.getByTestId('log-error-badge')).toBeInTheDocument();
    });
    expect(screen.getByTestId('log-error-badge')).toHaveTextContent('log: error');
  });

  it('does not show warn or error log badge when log.maxLevel is absent', async () => {
    render(<RequestList />);
    await waitFor(() => expect(screen.getAllByTestId('request-row')).toHaveLength(2));
    expect(screen.queryByTestId('log-warn-badge')).not.toBeInTheDocument();
    expect(screen.queryByTestId('log-error-badge')).not.toBeInTheDocument();
  });

  it('shows log count badge when log.count tag is present and > 0', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(JSON.stringify(makeResponse({ 'log.count': '3' })), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
      ) as unknown as typeof fetch,
    );
    render(<RequestList />);
    await waitFor(() => {
      expect(screen.getByTestId('log-count-badge')).toBeInTheDocument();
    });
    expect(screen.getByTestId('log-count-badge')).toHaveTextContent('log: 3');
  });

  it('does not show log count badge when log.count is 0', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(JSON.stringify(makeResponse({ 'log.count': '0' })), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
      ) as unknown as typeof fetch,
    );
    render(<RequestList />);
    await waitFor(() => expect(screen.getAllByTestId('request-row')).toHaveLength(2));
    expect(screen.queryByTestId('log-count-badge')).not.toBeInTheDocument();
  });

  it('shows dump count badge when dump.count tag is present and > 0', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(JSON.stringify(makeResponse({ 'dump.count': '2' })), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
      ) as unknown as typeof fetch,
    );
    render(<RequestList />);
    await waitFor(() => {
      expect(screen.getByTestId('dump-count-badge')).toBeInTheDocument();
    });
    expect(screen.getByTestId('dump-count-badge')).toHaveTextContent('dump: 2');
  });

  it('does not show dump count badge when dump.count is absent', async () => {
    render(<RequestList />);
    await waitFor(() => expect(screen.getAllByTestId('request-row')).toHaveLength(2));
    expect(screen.queryByTestId('dump-count-badge')).not.toBeInTheDocument();
  });
});
